# Calcpad S3 Server

A shared library and project store for Calcpad worksheets. It is an ASP.NET Core API (`Calcpad.S3Server`) in front of [Garage](https://garagehq.deuxfleurs.fr/), accessed through the AWS S3 SDK, with a SQLite user store.

The desktop app and VS Code don't talk to this server directly. The local Calcpad server signs in through `/api/s3` (see [../backend/API_SCHEMA.md](../backend/API_SCHEMA.md)) and mirrors files into an `s3cache/` folder next to its `logs/` folder.

## Run

```sh
cp .env.example .env    # fill in the secrets
docker compose up --build
```

On first start, `garage-init` assigns the Garage layout and creates an access key that is allowed to create buckets. The S3 server seeds an admin user from `ADMIN_USERNAME`/`ADMIN_PASSWORD` when the user store is empty. The API listens on `http://localhost:9430` (set with `S3_SERVER_PORT`).

## Worksheets

| Syntax | Meaning |
|--------|---------|
| `#LibraryPath {s3:my-lib}`<br>`#ProjectPath {s3:job-1042}` | Point a path root at a bucket |
| `#include {s3:my-lib}/steel/beam.cpd` | The latest version, by path |
| `#include {library}/steel/<baseId>` | The latest version of that file |
| `#include {library}/steel/<id>` | That exact version |
| `#read M from {s3:my-lib}/data/<id>` | Data files work by id too. The extension comes from the resolved file |

A path segment with no extension that parses as a GUID is looked up by id:

- **Locally:** the `.cpd`/`.cpdz` footers in that folder are scanned.
- **On S3:** the object metadata under that prefix is searched.

## Versions and ids

Every upload does the following:

- Keeps the file's `baseId`. It comes from the existing object, or the file's footer on the first upload, or is generated.
- Bumps `version`.
- Assigns a new `id`.

These, plus `lastUpdatedBy`/`lastUpdatedAt`, are written to both the worksheet's `fileInfo` footer and the S3 object metadata (`x-amz-meta-base-id`, `-id`, `-version`, `-sha256`, …).

Garage has no bucket versioning, so the previous version is copied to `.versions/{key}/{id}{ext}` before it is overwritten.

An upload must be based on the latest version. Its footer `id` (or the `If-Match` header) has to match the latest version's `id`, otherwise the upload is rejected with 409. `If-Match: *` overwrites anyway.

## Access levels

Users have a global role: `Viewer` (read), `Contributor` (upload and delete), or `Admin` (buckets, users and grants). A per-bucket grant overrides the global role, and a `None` grant denies access. Global admins are always admins.

## API

All routes except login require `Authorization: Bearer <token>`.

| Route | Role |
|-------|------|
| `POST /api/auth/login` | — |
| `POST /api/auth/register`, `GET\|PUT\|DELETE /api/users[/{id}]` | Admin |
| `GET /api/auth/profile` | any |
| `GET /api/buckets` | lists the buckets you can read |
| `POST /api/buckets` `{ name, kind: "Library"\|"Project" }`, `DELETE /api/buckets/{b}` | Admin |
| `GET\|PUT /api/buckets/{b}/grants`, `DELETE /api/buckets/{b}/grants/{userId}` | Admin |
| `GET /api/buckets/{b}/objects?prefix=&versions=` | Viewer |
| `GET /api/buckets/{b}/objects/{key}` (supports `If-None-Match: <id>`), `GET /api/buckets/{b}/meta/{key}` | Viewer |
| `PUT /api/buckets/{b}/objects/{key}` (supports `If-Match`), `DELETE …/objects/{key}` | Contributor |
| `GET /api/buckets/{b}/ids/{guid}[/content]?prefix=` | Viewer |
| `GET /api/buckets/{b}/ids/{baseId}/versions` | Viewer |

## Configuration

| Variable | Description |
|----------|-------------|
| `Jwt__Secret` | Required, at least 32 characters |
| `Admin__Username`, `Admin__Password` | Seed admin, used only when no users exist |
| `S3__ServiceURL`, `S3__AccessKey`, `S3__SecretKey`, `S3__Region` | Garage connection. Docker sets these from `garage-init` |
| `Data__Path` | SQLite file. Default `data/s3server.db` |
