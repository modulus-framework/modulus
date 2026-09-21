-- Rollback: Add_inbox_messages
-- Description: Removes the transactional inbox table.

DROP TABLE IF EXISTS administration.inbox_messages;
