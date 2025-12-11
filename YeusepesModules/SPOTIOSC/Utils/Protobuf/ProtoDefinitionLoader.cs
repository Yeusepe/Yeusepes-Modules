using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace YeusepesModules.SPOTIOSC.Utils.Protobuf
{
    /// <summary>
    /// Helper for loading protobuf definition files that are embedded as resources.
    /// These definitions can be used for reference or future protobuf code generation.
    /// </summary>
    internal static class ProtoDefinitionLoader
    {
        private static readonly Assembly ThisAssembly = typeof(ProtoDefinitionLoader).Assembly;

        // Cache by resource suffix (e.g. "SPOTIOSC.Proto.connect.proto")
        private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Load a .proto definition from embedded resources, using a suffix match.
        /// </summary>
        private static string LoadBySuffix(string resourceSuffix)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(resourceSuffix, out var cached))
                {
                    return cached;
                }

                // Resource names typically look like:
                // "YeusepesModules.SPOTIOSC.Proto.connect.proto"
                var resourceName = ThisAssembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to locate embedded proto resource with suffix '{resourceSuffix}'. " +
                        "Ensure the file is included as an <EmbeddedResource> in the .csproj.");
                }

                using var stream = ThisAssembly.GetManifestResourceStream(resourceName)
                                   ?? throw new InvalidOperationException(
                                       $"Embedded proto resource '{resourceName}' could not be opened.");
                using var reader = new StreamReader(stream);
                var definition = reader.ReadToEnd();

                Cache[resourceSuffix] = definition;
                return definition;
            }
        }

        /// <summary>
        /// Protobuf definition for spotify.connectstate (ClusterUpdate, SetVolumeCommand, etc.).
        /// Backed by SPOTIOSC/Proto/connect.proto.
        /// </summary>
        public static string GetConnectProto() => LoadBySuffix("SPOTIOSC.Proto.connect.proto");

        /// <summary>
        /// Protobuf definition for spotify.player.esperanto.proto.ContextPlayerState.
        /// Backed by SPOTIOSC/Proto/es_context_player_state.proto.
        /// </summary>
        public static string GetEsContextPlayerStateProto() =>
            LoadBySuffix("SPOTIOSC.Proto.es_context_player_state.proto");

        /// <summary>
        /// Protobuf definition for spotify.player.esperanto.proto.ContextPlayerOptions.
        /// Backed by SPOTIOSC/Proto/es_context_player_options.proto.
        /// </summary>
        public static string GetEsContextPlayerOptionsProto() =>
            LoadBySuffix("SPOTIOSC.Proto.es_context_player_options.proto");
    }
}


