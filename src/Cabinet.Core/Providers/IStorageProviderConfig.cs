﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cabinet.Core.Providers {
    public interface IStorageProviderConfig {
        string Delimiter { get; }
    }
}
