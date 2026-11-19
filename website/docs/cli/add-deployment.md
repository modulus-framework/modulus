# modulus add-deployment

Generates a container deployment for the app: a Compose file with a reverse proxy that serves HTTPS.

```bash
modulus add-deployment --host shop.example.com --email ops@example.com
modulus add-deployment --host shop.example.com --email ops@example.com --proxy nginx --monitoring
```

| Option | Meaning |
|---|---|
| `--host` | The public host name the API is served on, for example `shop.example.com`. It is the token issuer and the passkey relying party. |
| `--email` | The address Let's Encrypt sends certificate notices to. |
| `--proxy` | `traefik` (default; issues and renews its own certificates) or `nginx` (certificates are issued once with certbot; see the compose file). |
| `--monitoring` | Adds Prometheus (metrics), Jaeger (traces) and Grafana, bound to localhost, and sends the app's OpenTelemetry data to them. |

Set `Identity:Issuer` and the forwarded-headers settings to the public host the app is served on, so tokens and redirects use the right address behind the proxy.
