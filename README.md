# RavenDb-Demo
Takeaways from the RavenDB conference 2016 and overall cool features we don’t know much about.

## How do we load data?

1. Load<T>(idArray), Load<T1>().Include<T2>
2. Paged Querying
	* Clear session with Clear(), Evict()
3. Lazy operations
4. LoadStartingWith
5. Streaming API
6. Data subscriptions

## How do we insert data?

1. Bulk insert
2. Scripted patches via PutDocument
	* Debugging with output()
	* Custom JS functions
3. Check if document exists

## How do we update data?
1. Patches, scripted patches
2. Update by index
3. Batch execute commands of IDatabaseCommand
4. Scripted index

## Real-time updates and data caching
1. Changes API
2. Aggressive cache

## TODO:
1. Creating and configuring indexes
2. Full text search
3. Event sourcing
4. Replication
5. Side-by-side indexes
6. Sharding
7. Performance
8. Data structuring (working with identifiers, handling document relationships)
9. Map reduce concepts
10. Raven Studio (Admin, monitoring tools)
11. Out of the box bundles
12. Plugins (debugging)
13. Document store conventions
14. Monitoring