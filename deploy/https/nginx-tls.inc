# Mounted only by the optional operator HTTPS overlay.
listen 443 ssl;
ssl_certificate /run/strataai-tls/fullchain.pem;
ssl_certificate_key /run/strataai-tls/privkey.pem;
ssl_protocols TLSv1.2 TLSv1.3;
ssl_session_cache shared:StrataAITLS:10m;
ssl_session_tickets off;
