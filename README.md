# Npm CDN
![Coverage](https://raw.githubusercontent.com/p-ob/cdn/badges/coverage.svg)

## Overview
This project is an advanced .NET 10 Content Delivery Network (CDN) functioning as a pull-through cache for the public NPM registry. It allows applications to predictably resolve, cache, and serve static assets (such as HTML, CSS, JavaScript, and fonts) hosted within NPM packages, while offloading origin traffic securely and scaling effectively within Kubernetes.

## Core Features
- **Dynamic Version Resolution:** Handles semantic version ranges (e.g., `@latest`, `@4`, `@4.0`) by resolving them to exact literal versions via the public NPM registry.
- **Entrypoint Resolution:** Dynamically determines a package's default path by parsing the package's `exports`, `browser`, and `main` fields in `package.json` if a specific file is omitted in the request path.
- **Aggressive Extraction Strategy:** Rather than downloading individual files, it caches the whole package as an extracted `.tgz` tarball up-front on the first miss. Future file requests within the same package version are served instantaneously.
- **Storage Agnosticism:** Uses an `IStorageProvider` abstraction for caching files, currently supporting Kubernetes Persistent Volume mounts (with S3 on the roadmap).
- **Concurrency Locks:** Prevents overlapping writes or corruption during high-traffic cache misses ("cache stampedes") using thread-safe `SemaphoreSlim` locks.
- **Automated Eviction Policies:** Records `.last_accessed` timestamps using Unix epoch ticks, paving the way for a targeted background worker to evict stale caches over time (e.g., a default 30-day sliding window).
- **OpenTelemetry:** Generates fine-grained telemetry data and custom ActivitySources for distributed tracing.

## Endpoint API

All requests are mapped through the following endpoint structure:
`/npm/{packageName}@{version}/{*filePath}`

### Examples:
- **Exact Version Request:** `/npm/jquery@3.7.1/dist/jquery.js`
  - *Returns:* `Cache-Control: public, max-age=31536000, immutable` (1 year)
- **Fuzzy Version Request:** `/npm/jquery@3`
  - *Returns:* `Cache-Control: public, max-age=600` (10 minutes).
  - *Behavioral Note:* Resolves dynamically to the `3.x` latest release, falls back to checking `package.json` for the entry path, downloads the package (if a cache miss), and streams the entrypoint payload.
- **Scoped Packages:** `/npm/@types/jquery@3.5.30`

## Architecture & Code Organization
The solution is organized using the modern `.slnx` format and embraces Central Package Management (`Directory.Packages.props`, `Directory.Build.props`). TDD is heavily emphasized, heavily utilizing `TUnit` as the testing framework over xUnit.

```text
/
├── NpmCdn.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── NpmCdn.Api/             # Minimal APIs, routing, and HTTP pipeline cache headers
│   ├── NpmCdn.NpmRegistry/     # Handles NPM API communication, resolution, & tarball extraction
│   └── NpmCdn.Storage/         # Contains `IStorageProvider` and the persistent volume caching class
```

## License & Service Usage

### Software License
The code in this repository is licensed under the **MIT License**. You are free to fork, modify, and deploy your own instances of this CDN as you see fit. See [LICENSE](LICENSE) for details.

### Official Instance (cdn.pob.dev)
The official deployment of this software at **cdn.pob.dev** is a proprietary service.

* **No Public Grant:** The MIT license of this source code does **not** grant rights to use, proxy, or link to the `cdn.pob.dev` domain or its underlying infrastructure.
* **Restricted Access:** Usage of the `cdn.pob.dev` instance is strictly reserved for Patrick O'Brien and authorized projects. 
* **Unauthorized Traffic:** Any unauthorized use of the official instance may be rate-limited or blocked without notice.

## AI Agent Developer Notes 🤖
If you are an AI assistant continuing development on this repository, please heed the following details outlined by the primary human maintainer to ensure alignment with existing architectural patterns:

1. **Testing Environment Structure:** This repository uses `TUnit`. All test libraries utilize `[Before(Test)]` and Mocking (`Moq`). Treat nullable attributes meticulously. For standard properties, refer directly to Central Package Management mechanisms; individual `.csproj` configurations are minimal intentionally.

2. **Error Patterns:** 
   - When unit testing streams (`HttpContent.ReadAsStreamAsync` via mock setups), note that Http streams pulled from `GetAsync` with `HttpCompletionOption.ResponseHeadersRead` frequently evaluate `.Length` with a `NotSupportedException`. To verify valid streams in your tests, assert against `.CanRead` instead.
   - When making new network requests or file writes to directories, utilize `ActivitySource.StartActivity()` and `SetTag` correctly to ensure observability pipelines remain operational.

3. **Remaining Milestones (Roadmap Tasks):**
   - **AWS S3 Backend Implementation:** Add an integration for the `IStorageProvider` using AWSSDK.S3 to allow cloud-managed caching instead of block storage volumes.
   - **Background Sweeper Service:** The platform requires an `IHostedService` background worker to iterate over `GetStalePackagesAsync` and selectively call `DeletePackageVersionAsync` based on configurable timeouts (e.g., 30 days of inactivity).
   - **Kubernetes Integrations:** Scaffolding the `Dockerfile` and deploying standard K8s resources (Deployments, Services, ConfigMaps, PVCs, and Ingress routing).
