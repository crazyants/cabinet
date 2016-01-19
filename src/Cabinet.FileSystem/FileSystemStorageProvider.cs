﻿using Cabinet.Core;
using Cabinet.Core.Providers;
using Cabinet.Core.Results;
using Cabinet.FileSystem.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace Cabinet.FileSystem {
    internal class FileSystemStorageProvider : IStorageProvider<IFileCabinentConfig> {
        public const string ProviderType = "FileSystem";
        
        private readonly Func<IFileSystem> fileSystemFactory;

        public FileSystemStorageProvider(Func<IFileSystem> fileSystemFactory) {
            this.fileSystemFactory = fileSystemFactory;
        }
        
        /*
        public string GetUniqueFileName(string keyPrefix, string fileName) {
            // TODO - Prevent back paths
            string fullKeyPath = GetKeyFullPath(keyPrefix);
            
            string filePath = Path.Combine(fullKeyPath, fileName);

            string extensionlessName = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);
            int count = 1;

            while (this.fs.File.Exists(filePath)) {
                filePath = Path.Combine(fullKeyPath, String.Format("{0} ({1}){2}", extensionlessName, count, extension));
                count++;
            }

            return filePath;
        }
        */

        public Task<bool> ExistsAsync(string key, IFileCabinentConfig config) {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var fileInfo = this.GetFileInfo(key, config);
            return Task.FromResult(fileInfo.Exists);
        }

        public Task<IEnumerable<string>> ListKeysAsync(IFileCabinentConfig config, string keyPrefix = "", bool recursive = true) {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var cabinetFiles = GetFilesRecursive(keyPrefix, recursive, config);
            var keys = cabinetFiles.Select(f => CabinetFileInfo.GetFileKey(f, config.Directory));

            return Task.FromResult(keys);
        }

        public Task<ICabinetFileInfo> GetFileAsync(string key, IFileCabinentConfig config) {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var fileInfo = this.GetFileInfo(key, config);
            return Task.FromResult<ICabinetFileInfo>(new CabinetFileInfo(fileInfo, config.Directory));
        }

        public Task<IEnumerable<ICabinetFileInfo>> GetFilesAsync(IFileCabinentConfig config, string keyPrefix = "", bool recursive = true) {
            if (config == null) throw new ArgumentNullException(nameof(config));

            var cabinetFiles = GetFilesRecursive(keyPrefix, recursive, config);

            var cabinetFileInfos = cabinetFiles.Select(f => {
                return new CabinetFileInfo(f, config.Directory);
            });

            return Task.FromResult<IEnumerable<ICabinetFileInfo>>(cabinetFileInfos);
        }

        public async Task<ISaveResult> SaveFileAsync(string key, Stream content, HandleExistingMethod handleExisting, IFileCabinentConfig config) {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var fileInfo = this.GetFileInfo(key, config);

            var handleExisingResult = HandleExistingFile<ISaveResult>(fileInfo, key, handleExisting,
                () => new SaveResult(true) { AlreadyExists = true },
                (deleteResult) => new SaveResult(deleteResult.Exception, deleteResult.GetErrorMessage())
            );

            if(handleExisingResult != null) {
                return handleExisingResult;
            }

            try {
                var fs = fileSystemFactory();
                using (var writeStream = fs.File.OpenWrite(fileInfo.FullName)) {
                    await content.CopyToAsync(writeStream);

                    return new SaveResult(true);
                }
            } catch (Exception e) {
                return new SaveResult(e);
            }
        }

        public async Task<IMoveResult> MoveFileAsync(ICabinetFileInfo file, string destKey, HandleExistingMethod handleExisting, IFileCabinentConfig config) {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (String.IsNullOrWhiteSpace(destKey)) throw new ArgumentNullException(nameof(destKey));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var destFileInfo = this.GetFileInfo(destKey, config);
            
            var handleExisingResult = HandleExistingFile<IMoveResult>(destFileInfo, destKey, handleExisting,
                () => new MoveResult(true) { AlreadyExists = true },
                (deleteResult) => new MoveResult(deleteResult.Exception, deleteResult.GetErrorMessage())
            );

            if (handleExisingResult != null) {
                return handleExisingResult;
            }

            try {
                var fs = fileSystemFactory();

                if (file.ProviderType == ProviderType) {
                    var fileInfo = this.GetFileInfo(file.Key, config);
                    // Do file system move
                    fs.File.Move(fileInfo.FullName, destFileInfo.FullName);
                } else {
                    // Save file via stream
                    using (var fileStream = file.GetFileReadStream()) {
                        using (var destStream = fs.File.OpenWrite(destFileInfo.FullName)) {
                            await fileStream.CopyToAsync(destStream);
                        }
                    }
                }
                
                return new MoveResult(true);
            } catch (Exception e) {
                return new MoveResult(e);
            }
        }

        public Task<IDeleteResult> DeleteFileAsync(string key, IFileCabinentConfig config) {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var fileInfo = this.GetFileInfo(key, config);

            if (!fileInfo.Exists) {
                return Task.FromResult<IDeleteResult>(new DeleteResult(true) {
                    AlreadyDeleted = true
                });
            }

            var result = DeleteFile(fileInfo);
            return Task.FromResult(result);
        }

        public FileInfoBase GetFileInfo(string key, IFileCabinentConfig config) {
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentNullException(nameof(key));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (String.IsNullOrWhiteSpace(config.Directory)) {
                throw new ApplicationException("The config directory has not been configured.");
            }

            string fullKeyPath = GetKeyFullPath(config.Directory, key);

            var fs = fileSystemFactory();

            var keyFile = fs.FileInfo.FromFileName(fullKeyPath);
            var baseDir = fs.DirectoryInfo.FromDirectoryName(config.Directory);

            // Prevent backpaths
            if (!keyFile.Directory.IsSameDirectory(baseDir) && !keyFile.Directory.IsChildOf(baseDir)) {
                throw new ArgumentException(String.Format("{0} results in a path outside of {1}", key, baseDir.FullName), nameof(key));
            }

            return keyFile;
        }

        public DirectoryInfoBase GetDirectoryInfo(string key, IFileCabinentConfig config) {
            if (key == null) throw new ArgumentNullException(nameof(key)); // Allow empty strings
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (String.IsNullOrWhiteSpace(config.Directory)) {
                throw new ApplicationException("The config directory has not been configured.");
            }

            string fullKeyPath = GetKeyFullPath(config.Directory, key);

            var fs = fileSystemFactory();

            var keyFile = fs.DirectoryInfo.FromDirectoryName(fullKeyPath);
            var baseDir = fs.DirectoryInfo.FromDirectoryName(config.Directory);

            // Prevent backpaths
            if (!keyFile.IsSameDirectory(baseDir) && !keyFile.IsChildOf(baseDir)) {
                throw new ArgumentException(String.Format("{0} results in a path outside of {1}", key, baseDir.FullName), nameof(key));
            }

            return keyFile;
        }

        private string GetKeyFullPath(string directory, string key) {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            if (key == null) throw new ArgumentNullException(nameof(key)); // Allow empty strings

            string keyPath = Path.Combine(directory, key);
            string fullKeyPath = Path.GetFullPath(keyPath);

            return fullKeyPath;
        }

        private static IDeleteResult DeleteFile(FileInfoBase fileInfo) {
            try {
                fileInfo.Delete();
                return new DeleteResult(true);
            } catch (Exception e) {
                return new DeleteResult(e);
            }
        }
        
        private FileInfoBase[] GetFilesRecursive(string keyPrefix, bool recursive, IFileCabinentConfig config) {
            var dirInfo = this.GetDirectoryInfo(keyPrefix, config);

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = dirInfo.GetFiles("*", searchOption);
            return files;
        }

        private T HandleExistingFile<T>(FileInfoBase fileInfo, string key, HandleExistingMethod handleExisting, Func<T> createExisingResult, Func<IDeleteResult, T> createFailedDeleteResult) where T : class {
            if (!fileInfo.Exists) {
                return null;
            }

            if (handleExisting == HandleExistingMethod.Throw) {
                throw new ApplicationException(String.Format("File exists at {0} and handleExisting is set to throw", key));
            }

            if (handleExisting == HandleExistingMethod.Skip) {
                return createExisingResult();
            }

            if (handleExisting == HandleExistingMethod.Overwrite) {
                var deleteResult = DeleteFile(fileInfo);
                if (!deleteResult.Success) {
                    return createFailedDeleteResult(deleteResult);
                }

                return null;
            }

            throw new NotImplementedException();
        }
    }
}
