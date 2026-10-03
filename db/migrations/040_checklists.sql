BEGIN;
CREATE TABLE checklists (
 id uuid PRIMARY KEY CHECK(id <> '00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, card_id uuid NOT NULL,
 title text NOT NULL CHECK(length(title) BETWEEN 1 AND 160 AND title !~ '^[[:space:]]*$'),
 rank text COLLATE "C" NOT NULL CHECK(rank ~ '^[0-9]{30}$' AND rank > '000000000000000000000000000000' AND rank < '999999999999999999999999999999'),
 created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL CHECK(updated_at>=created_at),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0), deleted_at timestamptz,
 UNIQUE(id,tenant_id),
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 CHECK(deleted_at IS NULL OR deleted_at BETWEEN created_at AND updated_at)
);
CREATE UNIQUE INDEX ix_checklists_active_rank ON checklists(tenant_id,card_id,rank) WHERE deleted_at IS NULL;
CREATE TABLE checklist_items (
 id uuid PRIMARY KEY CHECK(id <> '00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, checklist_id uuid NOT NULL,
 text text NOT NULL CHECK(length(text) BETWEEN 1 AND 2000 AND text !~ '^[[:space:]]*$'),
 rank text COLLATE "C" NOT NULL CHECK(rank ~ '^[0-9]{30}$' AND rank > '000000000000000000000000000000' AND rank < '999999999999999999999999999999'),
 completed boolean NOT NULL DEFAULT false, completed_at timestamptz, completed_by uuid,
 created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL CHECK(updated_at>=created_at),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0), deleted_at timestamptz,
 FOREIGN KEY(checklist_id,tenant_id) REFERENCES checklists(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,completed_by) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 CHECK((completed AND completed_at IS NOT NULL AND completed_by IS NOT NULL AND completed_at BETWEEN created_at AND updated_at)
   OR (NOT completed AND completed_at IS NULL AND completed_by IS NULL)),
 CHECK(deleted_at IS NULL OR deleted_at BETWEEN created_at AND updated_at)
);
CREATE UNIQUE INDEX ix_checklist_items_active_rank ON checklist_items(tenant_id,checklist_id,rank) WHERE deleted_at IS NULL;
ALTER TABLE checklists ENABLE ROW LEVEL SECURITY;
ALTER TABLE checklists FORCE ROW LEVEL SECURITY;
CREATE POLICY checklists_tenant_isolation ON checklists
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
ALTER TABLE checklist_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE checklist_items FORCE ROW LEVEL SECURITY;
CREATE POLICY checklist_items_tenant_isolation ON checklist_items
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('040_checklists');
COMMIT;
