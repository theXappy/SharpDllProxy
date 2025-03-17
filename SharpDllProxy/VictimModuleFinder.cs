using PeNet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SharpDllProxy
{
    public static class VictimModuleFinder
    {
        public class VictimModuleInfo
        {
            public string ModuleName { get; set; }
            public string OriginalFilePath { get; set; }
            public int ExportCount { get; set; }
            public bool IsInWindowsDirectory { get; set; }
        }

        public static List<VictimModuleInfo> Search(Process process)
        {
            var moduleInfos = new List<VictimModuleInfo>();

            foreach (ProcessModule module in process.Modules)
            {
                var moduleInfo = new VictimModuleInfo
                {
                    ModuleName = module.ModuleName,
                    OriginalFilePath = module.FileName,
                    IsInWindowsDirectory = module.FileName.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase),
                    ExportCount = GetExportCount(module.FileName)
                };

                moduleInfos.Add(moduleInfo);
            }

            // Order the list from best victim to worst victim
            moduleInfos = moduleInfos
                .OrderBy(m => m.IsInWindowsDirectory)
                .ThenBy(m => m.ExportCount)
                .ToList();

            return moduleInfos;
        }

        private static int GetExportCount(string filePath)
        {
            try
            {
                var peFile = new PeFile(filePath);
                return peFile.ExportedFunctions?.Length ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
