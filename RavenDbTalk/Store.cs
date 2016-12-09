using Raven.Client;
using Raven.Client.Document;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RavenDbTalk
{
    class Store
    {
        private static Lazy<IDocumentStore> store = new Lazy<IDocumentStore>(CreateStore);

        public static IDocumentStore Current
        {
            get { return store.Value; }
        }

        private static IDocumentStore CreateStore()
        {
            IDocumentStore store = new DocumentStore()
            {
                Url = "http://localhost:9000",
                DefaultDatabase = "SampleDatabase"
            }.Initialize();

            return store;
        }
    }
}
