-- Meetup local Postgres bootstrap.
-- Creates one database per module (dbsh tracks each module in its own
-- database, so no cross-module version coordination is ever needed).
-- Mounted into /docker-entrypoint-initdb.d by docker-compose.yml; runs once
-- on first container start. Safe to re-run manually (IF NOT EXISTS).

SELECT 'CREATE DATABASE meetup_registrations' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'meetup_registrations')\gexec
SELECT 'CREATE DATABASE meetup_useraccess' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'meetup_useraccess')\gexec
SELECT 'CREATE DATABASE meetup_administration' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'meetup_administration')\gexec
SELECT 'CREATE DATABASE meetup_payments' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'meetup_payments')\gexec
SELECT 'CREATE DATABASE meetup_meetings' WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'meetup_meetings')\gexec
