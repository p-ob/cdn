# NpmCdn API

This document details the endpoint surface and behavior of the CDN.

## Request Structure

All requests match the following route template:
`/npm/{packageName}@{version}/{*filePath}`

### 1. `packageName`
The name of the NPM package. Supports scoped packages (e.g., `@organization/pkg`).

### 2. `version`
The package version you wish to request. This supports both:
- **Exact Literal Versions** (e.g., `1.2.3`)
- **Semantic Ranges & Tags** (e.g., `^1.2.0`, `latest`, `next`)

If a semantic range or tag is provided, the CDN dynamically resolves it against the public NPM registry to find the exact literal version.

### 3. `filePath` (Optional)
The path to the file within the package you want to fetch (e.g., `/dist/index.js`).

If omitted, the CDN employs an **Entrypoint Resolution** strategy, parsing the package's `package.json` to find the default file via the `exports`, `browser`, or `main` fields.

## Caching Headers

The CDN sets aggressive `Cache-Control` headers depending on the type of version requested.

### Exact Version Requests
When an exact literal version is requested (e.g., `/npm/jquery@3.7.1/dist/jquery.js`), the asset is considered immutable.
- **Cache-Control:** `public, max-age=31536000, immutable` (1 year)

### Fuzzy Version Requests
When a semantic range or tag is requested (e.g., `/npm/jquery@3` or `/npm/react@latest`), the asset could change when a new version is released. It is cached for a short duration.
- **Cache-Control:** `public, max-age=600` (10 minutes)

## Examples

**Exact file from an exact version:**
`/npm/jquery@3.7.1/dist/jquery.js`

**Default entrypoint from an exact scoped package version:**
`/npm/@types/jquery@3.5.30`
*(Resolves to the main file defined in package.json)*

**Exact file using a tag:**
`/npm/react@latest/umd/react.production.min.js`
