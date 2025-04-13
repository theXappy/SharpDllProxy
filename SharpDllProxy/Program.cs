using System;

namespace SharpDllProxy
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: SharpDllProxy.exe <orgDllPath> <payloadDllPath> <payloadFuncName>");
                return;
            }
            string orgDllPath = args[0];
            string payloadDllPath = args[1];
            string payloadFuncName = args[2];

            ProxyCreator proxyCreator = new ProxyCreator(Console.WriteLine);
            proxyCreator.CreateProxy(orgDllPath, payloadDllPath, payloadFuncName);
        }

    }
}
