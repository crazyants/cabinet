﻿using Cabinet.Core.Providers;
using Cabinet.Core.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cabinet.Core {
    public interface IFileCabinet {
        Task<bool> ExistsAsync(string key);
        Task<IEnumerable<string>> ListKeysAsync(string keyPrefix = "", bool recursive = true);

        Task<ICabinetFileInfo> GetFileAsync(string key);
        Task<IEnumerable<ICabinetFileInfo>> GetFilesAsync(string keyPrefix = "", bool recursive = true);

        Task<ISaveResult> SaveFileAsync(string key, Stream content, HandleExistingMethod handleExisting);
        Task<IMoveResult> MoveFileAsync(ICabinetFileInfo file, string destKey, HandleExistingMethod handleExisting);
        Task<IDeleteResult> DeleteFileAsync(string key);
    }
}
