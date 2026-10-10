DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM organization_type_upgrade_fixture f
  LEFT JOIN organizations o ON o.id=f.id
  WHERE o.id IS NULL OR o.organization_type<>'GENERIC'
   OR to_jsonb(o)-'organization_type' IS DISTINCT FROM f.original) THEN
  RAISE EXCEPTION 'Organization type upgrade changed legacy classification or payload';
 END IF;
 IF (SELECT column_default FROM information_schema.columns
  WHERE table_schema='public' AND table_name='organizations' AND column_name='organization_type')
  IS DISTINCT FROM '''STRATA''::text' THEN
  RAISE EXCEPTION 'New Organization classification does not default to Strata';
 END IF;
END $$;

BEGIN;
DO $$ DECLARE target uuid; before jsonb;
BEGIN
 SELECT id,to_jsonb(o) INTO target,before FROM organizations o LIMIT 1;
 BEGIN
  UPDATE organizations SET organization_type='HOA' WHERE id=target;
  RAISE EXCEPTION 'Unversioned classification mutation was admitted';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM<>'Organization classification requires an audited configuration command' THEN RAISE; END IF;
 END;
 IF (SELECT to_jsonb(o) FROM organizations o WHERE id=target) IS DISTINCT FROM before THEN
  RAISE EXCEPTION 'Refused classification changed protected Organization state';
 END IF;
 UPDATE organizations SET organization_type=organization_type WHERE id=target;
 IF (SELECT to_jsonb(o) FROM organizations o WHERE id=target) IS DISTINCT FROM before THEN
  RAISE EXCEPTION 'Classification no-op changed Organization state';
 END IF;
END $$;
ROLLBACK;
