namespace SharpDllProxy
{
    public class ProxyCreatorResults
    {
        /// <summary>
        /// The newly compiled DLL, that will call the payload on every function call
        /// and forward the call to the renamed original DLL.
        /// </summary>
        public string OutputDll { get; set; }

        /// <summary>
        /// Path to the renamed copy of the original DLL. Must be copied together with the
        /// output DLL, as its name is hardcoded in the output DLL.
        /// </summary>
        public string ProxiedDll { get; set; }
    }
}
