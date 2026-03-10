# Architecture

The CDN acts as a powerful pull-through cache for the public NPM registry, designed with scalability and high-concurrency in mind.

## Core Operations

### 1. Version Resolution
When a request arrives with a semantic version (e.g., `^1.0.0`) or tag (`latest`), the application queries the upstream NPM registry to resolve the alias to an exact literal version (e.g., `1.0.5`). 

### 2. Upfront Extraction Strategy
The CDN is heavily optimized for repeated access. Rather than lazily downloading individual files from a package when requested, the application downloads the *entire package tarball* (`.tgz`) from NPM upon the first cache miss. 
The entire package is extracted into the storage backend. Subsequent requests for *any* file within that package version are served instantaneously from the backend storage.

### 3. Concurrency
During a cache miss, multiple simultaneous requests for the same package version could trigger a "cache stampede." 
To prevent this and avoid overlapping writes or file corruption, the CDN utilizes thread-safe `SemaphoreSlim` locks, ensuring only a single thread downloads and extracts a specific package version while other requests wait for the operation to complete.

## Storage Backend

The application uses an `IStorageProvider` abstraction for caching extracted files. 

Currently, the supported implementations are:
- **Kubernetes Persistent Volume Mounts:** Caches files directly to a mounted NAS/block storage volume. Requires `NpmCdn.Storage`.
- **AWS S3:** Drops files into an S3 bucket for cloud-managed, inherently scalable storage. Requires `NpmCdn.Storage.Aws`.

## Eviction Policies & Background Sweeper

To prevent the storage backend from growing indefinitely, the CDN maintains `.last_accessed` timestamps using Unix epoch ticks for every package version requested.

An `IHostedService` background worker (the Background Sweeper Service) periodically iterates over cached packages using `GetStalePackagesAsync`. Packages that have not been accessed within a configurable timeout window (defaulting to 30 days) are evicted entirely from storage via `DeletePackageVersionAsync`.
