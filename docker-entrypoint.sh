#!/bin/sh
set -eu

if [ -n "${AD_CA_CERT_PATH:-}" ]; then
  if [ ! -f "$AD_CA_CERT_PATH" ]; then
    echo "AD_CA_CERT_PATH is set but the file does not exist: $AD_CA_CERT_PATH" >&2
    exit 1
  fi

  cp "$AD_CA_CERT_PATH" /usr/local/share/ca-certificates/ad-group-user-compare-ca.crt
  update-ca-certificates
fi

exec "$@"
