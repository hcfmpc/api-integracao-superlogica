PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS condominios (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    nome             TEXT    NOT NULL,
    numero_contrato  TEXT    NOT NULL,
    numero_conta     TEXT    NOT NULL,
    cooperativa      TEXT    NOT NULL,
    credenciais_enc  BLOB    NOT NULL,
    ativo            INTEGER NOT NULL DEFAULT 1,
    criado_em        TEXT    NOT NULL,
    atualizado_em    TEXT    NOT NULL
);

CREATE TABLE IF NOT EXISTS execucoes (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    condominio_id   INTEGER NOT NULL REFERENCES condominios(id),
    data_inicial    TEXT    NOT NULL,
    data_final      TEXT    NOT NULL,
    status          TEXT    NOT NULL,
    total_registros INTEGER,
    mensagem_erro   TEXT,
    executado_em    TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_execucoes_condominio ON execucoes(condominio_id);
CREATE INDEX IF NOT EXISTS idx_execucoes_status     ON execucoes(status);

CREATE TABLE IF NOT EXISTS idempotencia (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    hash          TEXT    NOT NULL UNIQUE,
    condominio_id INTEGER NOT NULL REFERENCES condominios(id),
    executado_em  TEXT    NOT NULL,
    status        TEXT    NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_idempotencia_hash ON idempotencia(hash);
