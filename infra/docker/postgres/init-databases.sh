#!/bin/bash
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
  SELECT 'CREATE DATABASE entitlex_platform'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'entitlex_platform')\gexec

  SELECT 'CREATE DATABASE cloudshield'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'cloudshield')\gexec

  SELECT 'CREATE DATABASE networkguard'
  WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'networkguard')\gexec
EOSQL
