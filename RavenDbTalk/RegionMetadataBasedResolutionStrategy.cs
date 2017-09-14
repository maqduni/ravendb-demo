using Raven.Client.FileSystem;
using Raven.Client.FileSystem.Shard;
using Raven.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RavenDbTalk
{
    public class RegionMetadataBasedResolutionStrategy : IShardResolutionStrategy
    {
        private int counter;
        private readonly IList<string> shardIds;
        private readonly ShardStrategy.ModifyFileNameFunc modifyFileName;
        private readonly FilesConvention conventions;

        public RegionMetadataBasedResolutionStrategy(IList<string> shardIds, ShardStrategy.ModifyFileNameFunc modifyFileName, FilesConvention conventions)
        {
            this.shardIds = shardIds;
            this.modifyFileName = modifyFileName;
            this.conventions = conventions;
        }

        public ShardResolutionResult GetShardIdForUpload(string filename, RavenJObject metadata)
        {
            var shardId = GenerateShardIdFor(filename, metadata);

            return new ShardResolutionResult
            {
                ShardId = shardId,
                NewFileName = modifyFileName(conventions, shardId, filename)
            };
        }

        public string GetShardIdFromFileName(string filename)
        {
            if (filename.StartsWith("/"))
                filename = filename.TrimStart(new[] { '/' });
            var start = filename.IndexOf(conventions.IdentityPartsSeparator, StringComparison.OrdinalIgnoreCase);
            if (start == -1)
                throw new InvalidDataException("file name does not have the required file name");

            var maybeShardId = filename.Substring(0, start);

            if (shardIds.Any(x => string.Equals(maybeShardId, x, StringComparison.OrdinalIgnoreCase)))
                return maybeShardId;

            throw new InvalidDataException("could not find a shard with the id: " + maybeShardId);
        }

        public string GenerateShardIdFor(string filename, RavenJObject metadata)
        {
            // choose shard based on the region
            var region = metadata.Value<string>("Region");

            string shardId = null;

            if (string.IsNullOrEmpty(region) == false)
                shardId = shardIds.FirstOrDefault(x => x.Equals(region, StringComparison.OrdinalIgnoreCase));

            return shardId ?? shardIds[Interlocked.Increment(ref counter) % shardIds.Count];
        }

        public IList<string> PotentialShardsFor(ShardRequestData requestData)
        {
            // for future use
            throw new NotImplementedException();
        }
    }
}
