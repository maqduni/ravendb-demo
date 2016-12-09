using NUnit.Framework;
using Raven.Abstractions.Commands;
using Raven.Abstractions.Data;
using Raven.Client;
using Raven.Client.Document;
using Raven.Json.Linq;
using RavenDbTalk.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RavenDbTalk
{
    class Program
    {
        static void Main(string[] args)
        {
            /*
             * Run functions from here if you don't have a test runner in VisualStudio
             */

            Console.WriteLine("Done!");
            Console.Read();
        }

        [TestCase]
        public static void G11_LoadInclude()
        {
            /*
             * Load multiple documents in a single request into session
             */
            using (var session = Store.Current.OpenSession())
            {
                var products = session.Load<Product>(new[] {
                    "products/77",
                    "products/76"
                });

                /*
                 * Loaded from session, i.e. no additional requests to the server are made
                 */
                var product77 = session.Load<Product>(77);
                var product76 = session.Load<Product>(76);
            }

            /*
             * Load a document and include another document in a single request
             */
            using (var session = Store.Current.OpenSession())
            {
                var order = session
                    .Include<Order>(o => o.Company)
                    .Load<Order>(826);

                var company = session.Load<Company>(order.Company);
            }

            /*
             * Load a document and include several documents of different datatypes in a single request
             */
            {
                MultiLoadResult result = Store.Current
                    .DatabaseCommands
                    .Get(ids: new[] { "orders/827" }, includes: new[] { "Company", "Employee", "ShipVia" });

                RavenJObject order = result.Results[0];
                RavenJObject company = result.Includes[0];
                RavenJObject employee = result.Includes[1];
                RavenJObject shipper = result.Includes[2];
            }
        }
        [TestCase]
        public static void G12_Lazily()
        {
            /*
             * There are cases when we need to retrieve multiple document results to perform a given operation.
             * RavenDB optimizes this scenario by sending multiple requests in a single round trip to the server
             */
            using (var session = Store.Current.OpenSession())
            {
                Lazy<Employee> employeeLazy = session.Advanced.Lazily.Load<Employee>("employees/1");
                Employee employee = employeeLazy.Value;

                Lazy<IEnumerable<Order>> ordersLazy = session.Query<Order>().Where(o => o.Employee == "employees/1").Take(20).Lazily();
                IEnumerable<Order> orders = ordersLazy.Value;
            }

            /*
             * Executing all pending lazy operations
             */
            using (var session = Store.Current.OpenSession())
            {
                Lazy<Employee> employeeLazy = session.Advanced.Lazily.Load<Employee>("employees/1");
                Lazy<IEnumerable<Order>> ordersLazy = session.Query<Order>().Where(o => o.Employee == "employees/1").Take(20).Lazily();

                session.Advanced.Eagerly.ExecuteAllPendingLazyOperations();

                Employee employee = employeeLazy.Value;
                IEnumerable<Order> orders = ordersLazy.Value;
            }
        }
        [TestCase]
        public static void G13_PagedQuerying()
        {
            /*
             * RavenDB was built safe by default. This means that it protects the client (and the server) from overflowing memory by reading
             * large query results into memory. Therefore, all queries utilize paging to read results in chunks. If a page size value is not
             * specified, the client API will limit the results to 128 documents. The server side also enforces it's own hard limit to the page
             * size of 1,024 results.
             * 
             * * That's why me may have a memory overflow when we use GetAll method.
             */
            using (var session = Store.Current.OpenSession())
            {
                int page = 2,
                    pageSize = 10;

                RavenQueryStatistics stats;
                IQueryable<Order> query = session.Query<Order>().Statistics(out stats)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                List<Order> orders = query.ToList();

                /*
                 * IMPORTANT!: This function stops tracking of all existing session entities and will remove all pending commands. Basically
                 * it wipes the entities out from memory.
                 */
                session.Advanced.Clear();
            }
        }
        [TestCase]
        public static void G14_LoadStartingWith()
        {
            /*
             * To load multiple entities that contain common prefix. You can Use wildcard symbols to match or exclude keys.
             * * The wildcard option is a bit more expensive than plain call since the documents are filtered on the server side
             *   after they are loaded from the data store.
             * * Another important benefit of retrieving by key is that eventual consistency issues are avoided since the data
             *   store is consistent immediately after writes.
             * 
             *   
             * Return paged entities with Id that starts with 'orders'.
             */
            using (var session = Store.Current.OpenSession())
            {
                int page = 2,
                    pageSize = 10;

                var pagingInformation = new RavenPagingInformation();
                Order[] result = session
                    .Advanced
                    .LoadStartingWith<Order>("orders", null, (page - 1) * pageSize, pageSize, null, pagingInformation);
            }

            /*
             * Return up to 128 entities with Id that starts with 'employees/' 
             * and rest of the key begins with "1" or "2" e.g. employees/10, employees/25
             * and rest of the key doens't begin with "6" or "7"
             * and skip all results until given key is found, return results after it
             */
            using (var session = Store.Current.OpenSession())
            {
                string keyPrefix = null;    // "1*|2*"
                string exclude = null;      // "6*|7*"
                string skipAfter = null;    // "employees/5"

                Employee[] result = session
                    .Advanced
                    .LoadStartingWith<Employee>("employees/", keyPrefix, 0, 128, exclude, null, skipAfter);
            }


            // We can represent more complex relationships using the URL definition, for example if we want to store each line of an order independently.
            // * This allows efficient loading of a document directly by ID.
            // * Using keys is the fastest method since RavenDB can go directly to the data store to load documents.

            /* Copy order lines to path orders/[int]/lines/[int]
             * 
               this.Lines.forEach(function (element, index, array) {
                   PutDocument(__document_id + '/lines/', element, {'Raven-Entity-Name': 'OrderLines'})
               });
             */
            using (var session = Store.Current.OpenSession())
            {
                OrderLine[] result = session
                    .Advanced
                    .LoadStartingWith<OrderLine>("orders/800/lines", null, 0, 128, null, null, null);
            }
        }
        [TestCase]
        public static void G15_StreamingAPI()
        {
            /*
             * Be aware that the results returned by the Stream() method are not tracked by session and, therefore, will not
             * be automatically saved if modified. Also, the Stream() method uses RavenDB's export facility which snapshots the
             * results when export starts. If a document in the result is added or deleted while the results are streaming, this
             * change will not be reflcted in the results.
             * 
             * It also consumes less memory than a Query executed not through the Streaming API, it's due to the fact that in this 
             * session doesn't track the result documents in memory. So for memory sensitive systems this may make sense.
             * Although it requires less memory, it's also about 1.5 times slower than a regular query.
             * 
             * A drawback (probably one of the drawbacks) that I noticed is that it doesn't respect DocumentStore caching, the results
             * are always retrieved from the server
             */
            using (var session = Store.Current.OpenSession())
            {
                int page = 2,
                    pageSize = 10;

                IQueryable<Order> query = session.Query<Order>("Orders/Totals")
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                QueryHeaderInformation queryHeaderInformation;
                IEnumerator<StreamResult<Order>> results = session.Advanced.Stream(query, out queryHeaderInformation);

                List<Order> orders = new List<Order>();
                while (results.MoveNext())
                {
                    StreamResult<Order> employee = results.Current;
                    orders.Add(employee.Document);
                }
            }


            /*
             * Another way, using the StartsWith syntax
             */
            using (var session = Store.Current.OpenSession())
            {
                RavenPagingInformation ravenPagingInformation = new RavenPagingInformation();
                IEnumerator<StreamResult<Order>> results = session.Advanced
                    .Stream<Order>("orders/", "8?", 0, 128, ravenPagingInformation, null);

                List<Order> orders = new List<Order>();
                while (results.MoveNext())
                {
                    StreamResult<Order> employee = results.Current;
                    orders.Add(employee.Document);
                }
            }
        }
        [TestCase]
        public static void G16_DataSubscriptions()
        {
            /*
             * There are cases when database is used as the data source for processing of application jobs. In most cases job processing
             * requires large amounts of records. This built-in API makes mainting job state much simpler and more efficient.
             * Note that subscriptions are persistent, so you are expected to hold on to the id and make use of it.
             * 
             * Documents are always sent in Etag order, which means that already processed ones will never be sent again over this subscription.
             * 
             * The nice thing about it is that it can be brought back up after a crash. Behind the scenes, once you have successfully
             * processed a document, a confirmation is sent to the server with the Etag of the most recently processed document. So when the
             * subscription opens again, the server will send a document with the corresponding next Etag.
             * * Creates a separate connection with datbase per subscription
             * * Internally uses Changes API to receive document updates
             * 
             * By default if the data subscription handler fails on error, it'll stop pulling documents and will close the subscritpion
             * connection immediately. If set IgnoreSubscriberErrors to true, errors raised by the handler will be ignored 
             * and it'll keep retrieving next docs.
             * * This means that the API ensures that you receive each document at least once.
             */
            var dsId = 1L;
            var dsConfigs = Store.Current.Subscriptions.GetSubscriptions(0, 128);
            if (!dsConfigs.Any())
            {
                dsId = Store.Current.Subscriptions.Create(new SubscriptionCriteria
                {
                    BelongsToAnyCollection = new[] { "orders" },
                    PropertiesMatch = new Dictionary<string, RavenJToken>()
                    {
                        {"ShipTo.Country", "Germany"}
                    }
                });
            } else
            {
                dsId = dsConfigs.First().SubscriptionId;

                /*
                 * Just in case release an active connection
                 */
                Store.Current.Subscriptions.Release(dsId);
            }
            Console.WriteLine($"Subscription {dsId}");

            var orders = Store.Current.Subscriptions.Open<Order>(dsId, new SubscriptionConnectionOptions()
            {
                BatchOptions = new SubscriptionBatchOptions()
                {
                    MaxDocCount = 16 * 1024,        // max number of docs in a single batch
                    MaxSize = 4 * 1024 * 1024,      // max total batch size in bytes
                    AcknowledgmentTimeout =         // max time needed to confirm that batch's been successfully processed
                    TimeSpan.FromMinutes(1) 
                },
                IgnoreSubscribersErrors = false,
                ClientAliveNotificationInterval = TimeSpan.FromSeconds(30) // heart beat intervals
            });

            using (var subscriber = orders.Subscribe(x =>
            {
                Console.WriteLine($"Received order {x.Id}");
            }))
            {
                Console.WriteLine("Stop subscription?");
                Console.Read();
                Store.Current.Subscriptions.Release(dsId);
            }

            //dsConfigs.ForEach(c =>
            //{
            //    Store.Current.Subscriptions.Delete(c.SubscriptionId);
            //});
        }


        [TestCase]
        public static void G21_BulkInsert()
        {
            /*
             * When loading large amounts of data, there is a certain overhead with the standard API that becomes expensive. RavenDB 2.0
             * introduced a bulk insert API to solve this problem. It is usually orders of magnitude faster than sending batches through
             * the standard API.
             * 
             * The bulk insert not only optimizes sending the data over the network. Instead of sending data on each call to the Store()
             * method, data is transmitted in batches. These batches are processed on the server, concurrent to the next batch being prepared
             * on the client. The default batch size is 512 documents.
             * 
             * Limitations:
             * - The entity ID must be provided by the client. When using the .NET client library this is not a problem (i.e. it may be
             *   a problem when using the plain HTTP API) since this happens automatically using the HiLo algorithm.
             * - Transactions are per batch, not for the entire operation. This  means that an error in one of the documents will result
             *   in the entire batch of documents being rolled back, but will not roll back the previously committed batches. By default
             *   the batch size is 512 documents, but this is configurable.
             * - Insert will not raise notifications for documents through Changes API. It means if you're using aggressive cache, the
             *   inserted documents won't appear in the cache until your cache period expires.
             * - Put triggers will execute, but the AfterCommit triggers won't.
             */
            using (BulkInsertOperation bulkInsert = Store.Current.BulkInsert())
            {
                for (int i = 0; i < 100 * 100; i++)
                {
                    bulkInsert.Store(new EmpoyeeForBatches
                    {
                        FirstName = "FirstName #" + i,
                        LastName = "LastName #" + i
                    });
                }
            }

            /*
             * Wait
             */
            Console.ReadLine();
        }
        [TestCase]
        public static void G22_ScriptedPatchInsert()
        {
            /*
             * Patch command is used to perform partial document updates without having to load, modify, and save
             * a full document. Scripted patches are usually useful for updating denormalized data in entities.
             * * If you are auditing changes, the audit records will have to be created from the scripted patch.
             * 
             * Custom functions are accessible in the patching API and SQL Replication bundle.
             * * To write custom functions, you need to create your own commonjs modules
             */
            
            /* 
            exports.clone = function (obj) {
                if (null === obj || "object" !== typeof obj) return obj;
                var copy = obj.constructor();
                for (var attr in obj) {
                    if (obj.hasOwnProperty(attr)) copy[attr] = obj[attr];
                }
                return copy;
            }
            */
            var patch = new ScriptedPatchRequest()
            {
                /*
                 * PutDocument can also update existing documents
                 */
                Script = @"
                                var newId = __document_id.replace('employees/', 'employeessp/');
                                output(newId);
                                if (!LoadDocument(newId)) {
                                    var clonedObj = clone(this);
                                    delete clonedObj['@metadata'];
                                    var putDoc = PutDocument(newId, clonedObj, {'Raven-Entity-Name': 'EmployeesSP'});   
                                }
                                "
            };

            Store.Current.DatabaseCommands.UpdateByIndex("Raven/DocumentsByEntityName",
                new IndexQuery { Query = "Tag:Employees" }, patch);

        }
        [TestCase]
        public static void G23_InsertWithVerification()
        {
            /*
             * Reduces the data sent over the network, therefore the time requried to transmit them.
             * 
             * Add a document
             */
            using (var session = Store.Current.OpenSession())
            {
                var newProduct = new Product()
                {
                    Name = "RavenDB",
                    PricePerUnit = 10.50M
                };

                long identity = Store.Current.DatabaseCommands.NextIdentityFor("products");
                session.Store(newProduct, "products/" + identity);
                session.SaveChanges();

                var docMetadata = Store.Current.DatabaseCommands.Head("products/" + identity);
                if (docMetadata == null)
                {
                    /*
                     * Insert the new document
                     */
                }
            }
        }


        //TODO: Look at the example of a recursive index (https://groups.google.com/forum/#!topic/ravendb/OC3nNJOJjmc)
        [TestCase]
        static public void G31_ScriptedIndexUpdate()
        {
            /*
             * Scripted Index Results bundle allows you to attach scripts to indexes. Those scripts can operate on the results of the 
             * indexing. This creates new oppportunities, such as modification of documents by index calculated values or recursive map/reduce indexes.
             * * In order to activate this bumndle, you need to add the ScriptedIndexResults to the Raven/ActiveBundles on creation of the database.
             * 
             * Once the bundle is activated, it adds a database index update trigger which runs when an index entry is created or deleted.
             * In order to add scripted results to an index, we need to to put a special document with the key Raven/ScriptedIndexResults/[IndexName]
             * that will hold the Index and Delete scipts.
             */
            using (var session = Store.Current.OpenSession())
            {
                session.Store(new ScriptedIndexResults
                {
                    Id = ScriptedIndexResults.IdPrefix + "Orders/ByCompany",
                    IndexScript = @"
			            var company = LoadDocument(this.Company);
			            if(company == null)
					            return;
			            company.Orders = { Count: this.Count, Total: this.Total };
			            PutDocument(this.Company, company);
		            ",
                    DeleteScript = @"
			            var company = LoadDocument(key);
			            if(company == null)
					            return;
			            delete company.Orders;
			            PutDocument(key, company);
		            "
                });
                session.SaveChanges();
            }
        }
        [TestCase]
        public static void G32_IDatabaseCommandBatchUpdate()
        {
            /*
             * To send multiple operations in a single request, reducing the number of remote calls and allowing several operations
             * to share same transaction, Batch should be used
             * * All operations in the batch will succeed or fail as a transaction.
             * 
             * - PutCommandData
             * - DeleteCommandData
             * - PatchCommandData
             * - ScriptedPatchCommandData
             */
            var batch = new List<ICommandData>()
            {
                new PutCommandData
                {
                    Key = "products/999",
                    Document = RavenJObject.FromObject(new Product
                    {
                        Name = "My Product",
                        Supplier = "suppliers/999",
                        PricePerUnit = 99
                    }),
                    Metadata = new RavenJObject()
                },
                new DeleteCommandData
                {
                    Key = "products/10000"
                },
                new PatchCommandData
                {
                    Key = "products/999",
                    Patches = new[]
                    {
                        new PatchRequest()
                        {
                            Type = PatchCommandType.Inc,
                            Name = "PricePerUnit",
                            Value = 2
                        }
                    }
                },
                new ScriptedPatchCommandData {
                    Key = "products/23",
                    Patch = new ScriptedPatchRequest()
                    {
                        Script = @"
						    var product = LoadDocument(newProductId);
                            if (product && product.PricePerUnit > 0) {
                                this.PricePerUnit = (product.PricePerUnit / 2);
                                output(this.PricePerUnit);
                            }",
                        Values = new Dictionary<string, object> {
                            { "newProductId", "products/999" }
                        }
                    }
                }
            };

            BatchResult[] results = Store.Current.DatabaseCommands.Batch(batch);
        }


        [TestCase]
        public static void G41_ChangesApiForDocuments()
        {
            /*
             * Receiving real-time updates from the database are made possible through Changes API. It allows to subscribe to a particluar
             * event through a set of event filters.
             * 
             * Depending on the type of the store you use (DocumentStore, ShardedDocumentStore or EmbeddableDocumentStore) you will get 
             * an appropriate instance which is an implementation of a common IDatabaseChanges interface.
             * * Uses Reactive Extensions
             * * Creates a single connection to the database per document store, it means you can make as many subscriptions as you'd like.
             * 
             * - ForAllDocuments
             * - ForDocument
             * - ForDocumentsInCollection
             * - ForDocumentsOfType
             * - ForDocumentsStartingWith
             */
            IDisposable subscribtion = Store.Current.Changes()
                .ForAllDocuments()
                .Subscribe(change => Console.WriteLine($"Changes API -> {change.Type} {change.Id}"));
            Console.WriteLine("Subscribed to changes for all documents");

            try
            {
                using (var session = Store.Current.OpenSession())
                {
                    /*
                     * Add new document
                     */
                    Console.WriteLine("Create a document?");
                    Console.ReadLine();

                    var newProduct = new Product()
                    {
                        Name = "RavenDB",
                        PricePerUnit = 10.50M
                    };

                    long identity = Store.Current.DatabaseCommands.NextIdentityFor("products");
                    session.Store(newProduct, "products/" + identity);
                    //session.Store(newProduct, "products/");
                    //string productId = session.Advanced.GetDocumentId(newProduct);
                    session.SaveChanges();


                    /*
                     * Update documents
                     */
                    Console.WriteLine("Update documents?");
                    Console.ReadLine();

                    var patchBatch = new List<ICommandData>()
                    {
                        new PatchCommandData()
                        {
                            Key = "products/" + identity,
                            Patches = new[]
                            {
                                new PatchRequest()
                                {
                                    Type = PatchCommandType.Inc,
                                    Name = "PricePerUnit",
                                    Value = 2
                                }
                            }
                        },
                        new ScriptedPatchCommandData() {
                            Key = "products/23",
                            Patch = new ScriptedPatchRequest()
                            {
                                Script = @"
						            var product = LoadDocument(newProductId);
                                    if (product && product.PricePerUnit > 0) {
                                        this.PricePerUnit = (product.PricePerUnit / 2);
                                        output(this.PricePerUnit);
                                    }",
                                Values = new Dictionary<string, object> {
                                    { "newProductId", "products/72" }
                                }
                            }
                        }
                    };
                    Store.Current.DatabaseCommands.Batch(patchBatch);


                    /*
                     * Delete a document
                     */
                    Console.WriteLine("Delete a document?");
                    Console.Read();

                    session.Delete(newProduct);
                    session.SaveChanges();
                }

                /*
                 * Do something directly from Raven Studio
                 */
                Console.Read();
            }
            finally
            {
                subscribtion?.Dispose();
            }
        }
        [TestCase]
        public static void G42_ChangesApiIndexes()
        {
            /*
             * - ForIndex
             * - ForAllIndexes
             */
            IDisposable subscription = Store.Current
            .Changes()
            .ForIndex("Orders/Totals")
            .Subscribe(
                change =>
                {
                    Console.WriteLine($"Changes API -> {change.Type}");

                    switch (change.Type)
                    {
                        case IndexChangeTypes.IndexAdded:
                            // do something
                            break;
                        case IndexChangeTypes.IndexDemotedToAbandoned:
                            // do something
                            break;
                        case IndexChangeTypes.IndexDemotedToIdle:
                            // do something
                            break;
                        case IndexChangeTypes.IndexPromotedFromIdle:
                            // do something
                            break;
                        case IndexChangeTypes.IndexRemoved:
                            // do something
                            break;
                        case IndexChangeTypes.MapCompleted:
                            // do something
                            break;
                        case IndexChangeTypes.ReduceCompleted:
                            // do something
                            break;
                        case IndexChangeTypes.RemoveFromIndex:
                            // do something
                            break;
                    }
                });

            try
            {
                /*
                 * Do something directly from Raven Studio
                 */
                Console.Read();
            }
            finally
            {
                subscription?.Dispose();
            }
        }
        [TestCase]
        public static void G43_ChangesApiTransformers()
        {
            /*
             * ForAllTransformers
             */
            IDisposable subscription = Store.Current
            .Changes()
            .ForAllTransformers()
            .Subscribe(
                change =>
                {
                    Console.WriteLine($"Changes API -> {change.Type}");

                    switch (change.Type)
                    {
                        case TransformerChangeTypes.TransformerAdded:
                            // do something
                            break;
                        case TransformerChangeTypes.TransformerRemoved:
                            // do something
                            break;
                    }
                });

            try
            {
                /*
                 * Do something directly from Raven Studio
                 */
                Console.Read();
            }
            finally
            {
                subscription?.Dispose();
            }
        }
        [TestCase]
        public static void G44_ChangesApiBulkInsert()
        {
            /*
             * ForBulkInsert
             */
            using (BulkInsertOperation bulkInsert = Store.Current.BulkInsert())
            {
                IDisposable subscription = Store.Current
                    .Changes()
                    .ForBulkInsert(bulkInsert.OperationId)
                    .Subscribe(change =>
                    {
                        Console.WriteLine($"Changes API -> {change.Type} {change.Id}");

                        switch (change.Type)
                        {
                            case DocumentChangeTypes.BulkInsertStarted:
                                // do something
                                break;
                            case DocumentChangeTypes.BulkInsertEnded:
                                // do something
                                break;
                            case DocumentChangeTypes.BulkInsertError:
                                // do something
                                break;
                        }
                    });

                try
                {
                    for (int i = 0; i < 100 * 100; i++)
                    {
                        bulkInsert.Store(new Employee
                        {
                            FirstName = "FirstName #" + i,
                            LastName = "LastName #" + i
                        });
                    }

                    /*
                     * Wait
                     */
                    Console.Read();
                }
                finally
                {
                    subscription?.Dispose();
                }
            }
        }
        [TestCase]
        public static void G45_ChangesApiDataSubscriptions()
        {
            /*
             * Track all data subscription changes
             * 
             * - ForDataSubscription
             * - ForAllDataSubscriptions
             */
            IDisposable subscription = Store.Current
            .Changes()
            .ForAllDataSubscriptions()
            .Subscribe(
                change =>
                {
                    var subscriptionId = change.Id;
                    Console.WriteLine($"Changes API -> {change.Type} {change.Id}");

                    switch (change.Type)
                    {
                        case DataSubscriptionChangeTypes.SubscriptionOpened:
                            // do something
                            break;
                        case DataSubscriptionChangeTypes.SubscriptionReleased:
                            // do something
                            break;
                    }
                });

            try
            {
                /*
                 * Open or release a subscription from another VisualStudio instance
                 */
                Console.Read();
            }
            finally
            {
                subscription?.Dispose();
            }
        }
        [TestCase]
        public static void G46_ChangesApiReplicationConflicts()
        {
            /*
             * Replication conflicts, for both documents and attachments can be tracked.
             * 
             * In RavenDB client you have an opportunity to register conflict listeners which are used to resolve
             * conflicted document. However this can happen only if you get the conflicted documents. The ability
             * to subscribe to the replication conflicts gives more power. Now if you listen to the conflicts and 
             * have any conflict listener registered, the client will automatically resolve the conflict right after
             * the arrival of the notification.
             * 
             * - ForAllReplicationConflicts
             */
            IDisposable subscription = Store.Current
                .Changes()
                .ForAllReplicationConflicts()
                .Subscribe(conflict =>
                {
                    if (conflict.ItemType == ReplicationConflictTypes.DocumentReplicationConflict)
                    {
                        Console.WriteLine("Conflict detected for {0}. Ids of conflicted docs: {1}. " +
                                          "Type of replication operation: {2}",
                                          conflict.Id,
                                          string.Join(", ", conflict.Conflicts),
                                          conflict.OperationType);
                    }
                });

            try
            {
                /*
                 * Wait
                 */
                Console.Read();
            }
            finally
            {
                subscription?.Dispose();
            }
        }

        [TestCase]
        public static void G51_AggressiveCache()
        {
            /*
             * Provided the data don't change over a specified period, the document store will return the data directly from it's local cache.
             * 
             * The document store subscribes to server notifictions via the Changes API and invalidates cached data whenever it receives a
             * notification for these data from the server.
             */
            // TODO: Invalidate on SaveChanges documentStore.Conventions.ShouldSaveChangesForceAggressiveCacheCheck = true;
            using (var session = Store.Current.OpenSession())
            using (session.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
            {
                int page = 2,
                    pageSize = 10;

                IQueryable<Order> query = session.Query<Order>()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                List<Order> orders = query.ToList();
                session.Advanced.Clear();
            }

            using (var session = Store.Current.OpenSession())
            using (session.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
            {
                int page = 2,
                    pageSize = 10;

                IQueryable<Order> query = session.Query<Order>()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                List<Order> orders = query.ToList();
                session.Advanced.Clear();
            }

            using (var session = Store.Current.OpenSession())
            {
                int page = 2,
                    pageSize = 10;

                IQueryable<Order> query = session.Query<Order>()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                List<Order> orders = query.ToList();
                session.Advanced.Clear();
            }

            using (var session = Store.Current.OpenSession())
            using (session.Advanced.DocumentStore.AggressivelyCacheFor(TimeSpan.FromMinutes(5)))
            {
                int page = 2,
                    pageSize = 10;

                IQueryable<Order> query = session.Query<Order>()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Where(x => x.Employee == "employees/1");

                List<Order> orders = query.ToList();
                session.Advanced.Clear();
            }
        }
    }
}
