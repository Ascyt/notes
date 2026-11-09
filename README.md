# notes

A self-hosted notes app: Markdown notes and other files in directories, edited in the browser, with permissions per directory and file. Users log in through Keycloak. It runs as a Docker Compose stack behind a Cloudflare tunnel, so the server needs no public IP and no open port.

## Quick start

1. Copy `.env.example` to `.env` and fill it in.
2. `docker compose up -d --build`

The whole setup (Cloudflare tunnel, Keycloak, the first admin) is in [Deployment](docs/deployment.md).

## Documentation

- [Deployment](docs/deployment.md): what the stack is made of, setting it up on a server, backups.
- [Sessions](docs/sessions.md): logging in, how long a session lasts, "Remember me".
- [Permissions](docs/permissions.md): who may see and change what, and how to write it down.
- [Kill switch](docs/kill-switch.md): shutting the whole site down at once, and bringing it back.
- [Development](docs/development.md): running the app locally, and the tests.
- [Security specification](docs/security-specification.md): the reference for what the permissions mean, for the code and its tests.

## License

[AGPL-3.0](LICENSE)
