-- PRD-01 FOUND-FR-009 / PRD-04: actual schema-69 preference upgraded to 71.
-- Observe a real transition, without attributing an unknown creation time.
BEGIN;
DO $checks$
DECLARE
 original user_board_preferences%ROWTYPE;
 changed user_board_preferences%ROWTYPE;
 started timestamptz;
 finished timestamptz;
 refused boolean;
BEGIN
 SELECT * INTO STRICT original FROM user_board_preferences ORDER BY board_id,user_id LIMIT 1;
 IF original.created_at IS NOT NULL OR original.version<>1 THEN
  RAISE EXCEPTION 'Expected an upgraded preference with unknown creation';
 END IF;
 started:=clock_timestamp();
 UPDATE user_board_preferences SET starred=NOT starred,version=version+1,updated_at=clock_timestamp()
 WHERE tenant_id=original.tenant_id AND id=original.id;
 finished:=clock_timestamp();
 SELECT * INTO STRICT changed FROM user_board_preferences WHERE tenant_id=original.tenant_id AND id=original.id;
 IF changed.created_at IS NOT NULL THEN
  RAISE EXCEPTION 'Legacy Board preference mutation manufactured a creation clock';
 END IF;
 IF changed.starred=original.starred OR changed.version<>2 OR NOT isfinite(changed.updated_at)
  OR changed.updated_at<original.updated_at OR changed.updated_at NOT BETWEEN started AND finished THEN
  RAISE EXCEPTION 'Legacy Board preference mutation lost its actual update clock';
 END IF;
 IF (SELECT count(*) FROM board_star_events WHERE tenant_id=original.tenant_id AND entity_id=original.id)<>1
  OR NOT EXISTS(SELECT FROM board_star_events WHERE tenant_id=original.tenant_id AND entity_id=original.id
   AND actor_id=original.user_id AND board_id=original.board_id AND version=2 AND created_at=changed.updated_at) THEN
  RAISE EXCEPTION 'Legacy Board preference history invented a baseline or lost the mutation clock';
 END IF;
 UPDATE user_board_preferences SET starred=starred WHERE tenant_id=original.tenant_id AND id=original.id;
 IF (SELECT to_jsonb(p) FROM user_board_preferences p WHERE tenant_id=original.tenant_id AND id=original.id) IS DISTINCT FROM to_jsonb(changed)
  OR (SELECT count(*) FROM board_star_events WHERE tenant_id=original.tenant_id AND entity_id=original.id)<>1 THEN
  RAISE EXCEPTION 'Legacy Board preference no-op rewrote source or history';
 END IF;
 BEGIN
  UPDATE user_board_preferences SET created_at=updated_at WHERE tenant_id=original.tenant_id AND id=original.id;
  RAISE EXCEPTION 'Legacy Board preference accepted invented creation';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 BEGIN
  UPDATE user_board_preferences SET updated_at=updated_at-interval '1 second' WHERE tenant_id=original.tenant_id AND id=original.id;
  RAISE EXCEPTION 'Legacy Board preference accepted a backwards update clock';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 refused:=false;
 BEGIN
  UPDATE board_star_events SET version=version WHERE tenant_id=original.tenant_id AND entity_id=original.id;
 EXCEPTION WHEN check_violation THEN refused:=true;
 END;
 IF NOT refused THEN RAISE EXCEPTION 'Legacy Board preference history allowed UPDATE'; END IF;
 refused:=false;
 BEGIN
  DELETE FROM board_star_events WHERE tenant_id=original.tenant_id AND entity_id=original.id;
 EXCEPTION WHEN check_violation THEN refused:=true;
 END;
 IF NOT refused THEN RAISE EXCEPTION 'Legacy Board preference history allowed DELETE'; END IF;
 IF (SELECT to_jsonb(p) FROM user_board_preferences p WHERE tenant_id=original.tenant_id AND id=original.id) IS DISTINCT FROM to_jsonb(changed)
  OR (SELECT count(*) FROM board_star_events WHERE tenant_id=original.tenant_id AND entity_id=original.id)<>1 THEN
  RAISE EXCEPTION 'Refused legacy clock/history writes changed protected state';
 END IF;
END $checks$;
-- Later upgrade fixtures retain their original baseline, including empty history.
ROLLBACK;
