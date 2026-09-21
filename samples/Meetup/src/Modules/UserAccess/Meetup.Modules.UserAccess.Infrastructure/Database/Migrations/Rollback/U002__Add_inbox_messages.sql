-- Rollback: Add_inbox_messages
-- Description: Removes the transactional inbox table.

DROP TABLE IF EXISTS useraccess.inbox_messages;
