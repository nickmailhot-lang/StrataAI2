-- ARCH-04 / FOUND-FR-004/009: transition hooks retain invoker security and canonical clock admission.
DO $$ BEGIN
 IF (SELECT count(*) FROM pg_trigger WHERE tgrelid='cards'::regclass AND NOT tgisinternal AND
  ((tgname='cards_sync_route' AND tgtype=9 AND tgfoid='sync_card_route()'::regprocedure)
   OR (tgname='cards_sync_inserted_routes' AND tgtype=4 AND tgnewtable='inserted_cards'
    AND tgfoid='sync_inserted_card_routes()'::regprocedure)
   OR (tgname='cards_sync_updated_routes' AND tgtype=16 AND tgnewtable='updated_cards'
    AND tgfoid='sync_updated_card_routes()'::regprocedure)))<>3
 THEN RAISE EXCEPTION 'Card synchronization hook contract changed'; END IF;
 IF EXISTS(SELECT 1 FROM pg_proc WHERE oid IN ('sync_inserted_card_routes()'::regprocedure,
   'sync_updated_card_routes()'::regprocedure) AND
   (prosecdef OR NOT COALESCE(proconfig @> ARRAY['search_path=pg_catalog, public'],false)))
 THEN RAISE EXCEPTION 'Batch projection security contract changed'; END IF;
 IF EXISTS(SELECT 1 FROM pg_proc p CROSS JOIN LATERAL aclexplode(COALESCE(p.proacl,acldefault('f',p.proowner))) a
 WHERE p.oid IN ('sync_inserted_card_routes()'::regprocedure,'sync_updated_card_routes()'::regprocedure)
 AND a.grantee=0 AND a.privilege_type='EXECUTE')
 THEN RAISE EXCEPTION 'Batch functions expose PUBLIC execution'; END IF;
 IF (SELECT count(*) FROM pg_class WHERE oid IN ('cards'::regclass,'card_routes'::regclass)
  AND relrowsecurity AND relforcerowsecurity)<>2
 THEN RAISE EXCEPTION 'Canonical/route RLS changed'; END IF;
END $$;
