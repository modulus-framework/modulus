-- Rollback: Add_inbox_messages
-- Description: Removes the transactional inbox table.

DROP TABLE IF EXISTS payments.inbox_messages;
