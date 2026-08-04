-- Проверяем существование и создаем таблицу заказов
CREATE TABLE IF NOT EXISTS "Orders" (
    "Id" UUID NOT NULL CONSTRAINT "PK_Orders" PRIMARY KEY, -- ИСПРАВЛЕНО НА UUID
    "CustomerId" VARCHAR(255) NOT NULL,
    "Amount" NUMERIC(18,2) NOT NULL,
    "Status" VARCHAR(50) NOT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL
);

-- Проверяем существование и создаем таблицу паттерна Transactional Outbox
CREATE TABLE IF NOT EXISTS "OutboxMessages" (
    "Id" UUID NOT NULL CONSTRAINT "PK_OutboxMessages" PRIMARY KEY, -- ИСПРАВЛЕНО НА UUID
    "Type" VARCHAR(255) NOT NULL,
    "Payload" TEXT NOT NULL,
    "OccurredOn" TIMESTAMP WITH TIME ZONE NOT NULL,
    "ProcessedAt" TIMESTAMP WITH TIME ZONE NULL
);
