
namespace UAEdgeHEMS
{
    using Opc.Ua;
    using Opc.Ua.Server;
    using System.Collections.Generic;

    public partial class UAServer : StandardServer
    {
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            List<INodeManager> nodeManagers = new List<INodeManager>
            {
                new UANodeManager(server, configuration)
            };

            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties
            {
                ManufacturerName = "Erich Barnstedt",
                ProductName = "UA Edge Home Energy Management System",
                ProductUri = "",
                SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
                BuildNumber = Utils.GetAssemblyBuildNumber(),
                BuildDate = Utils.GetAssemblyTimestamp()
            };

            return properties;
        }
    }
}
