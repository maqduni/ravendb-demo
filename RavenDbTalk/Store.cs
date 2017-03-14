using Raven.Client;
using Raven.Client.Document;
using Raven.Client.FileSystem;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RavenDbTalk
{
    class Store
    {
        private static readonly Lazy<IDocumentStore> documentStore = new Lazy<IDocumentStore>(CreateDocumentStore);
        private static readonly Lazy<IFilesStore> filesStore = new Lazy<IFilesStore>(CreateFilesStore);

        public static IDocumentStore Documents => documentStore.Value;
        public static IFilesStore Files => filesStore.Value;

        private static IDocumentStore CreateDocumentStore()
        {
            IDocumentStore store = new DocumentStore()
            {
                Url = "http://localhost:8080",
                DefaultDatabase = "SampleDatabase"
            }
            .Initialize();

            return store;
        }
        private static IFilesStore CreateFilesStore()
        {
            IFilesStore store = new FilesStore()
            {
                Url = "http://localhost:8080",
                DefaultFileSystem = "SampleFileSystem"
            }.Initialize();

            return store;
        }
    }
}
