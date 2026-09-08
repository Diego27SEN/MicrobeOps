#nullable enable

using Microsoft.Extensions.DependencyInjection;

// AddGameApiClient lives in .Apis.Extensions, not in .Apis. Easy to miss, and the failure is a
// compile error rather than anything subtle, so it is worth naming here.
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace Armada.CloudCode
{
    /// <summary>
    /// Dependency wiring for the module.
    /// <para>
    /// <c>config.AddGameApiClient()</c> is the supported registration; the older
    /// <c>GameApiClient.Create()</c> is obsolete and must not come back. Everything the functions
    /// need is registered here rather than constructed inside them, so the endpoints stay testable
    /// with a null context and a primed provider.
    /// </para>
    /// </summary>
    public class ModuleSetup : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.Dependencies.AddSingleton<ConfigProvider>();
            config.AddGameApiClient();
        }
    }
}
