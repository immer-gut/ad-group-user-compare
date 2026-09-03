#!/bin/sh
set -eu

mkdir -p /etc/ldap

if [ -n "${AD_CA_CERT_PATH:-}" ]; then
  if [ ! -f "$AD_CA_CERT_PATH" ]; then
    echo "AD_CA_CERT_PATH is set but the file does not exist: $AD_CA_CERT_PATH" >&2
    exit 1
  fi

  cp "$AD_CA_CERT_PATH" /usr/local/share/ca-certificates/ad-group-user-compare-ca.crt
  update-ca-certificates
fi

case "${AD_VERIFY_CERTIFICATE:-true}" in
  false|False|FALSE|0|no|No|NO)
    export LDAPTLS_REQCERT=never
    ;;
  *)
    export LDAPTLS_REQCERT=demand
    ;;
esac

export LDAPTLS_CACERT=/etc/ssl/certs/ca-certificates.crt
{
  echo "TLS_CACERT /etc/ssl/certs/ca-certificates.crt"
  echo "TLS_REQCERT $LDAPTLS_REQCERT"
} > /etc/ldap/ldap.conf

exec "$@"
