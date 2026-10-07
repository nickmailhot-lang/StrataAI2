-- ARCH-04/07 and PRD-03/60: direct server-side capability boundaries.
BEGIN;
DO $$
DECLARE bad_limit integer; bad_worker uuid; refused boolean;
BEGIN
 SET LOCAL ROLE strataai_worker_runtime;
 FOREACH bad_limit IN ARRAY ARRAY[NULL,0,101] LOOP
  refused:=false;
  BEGIN PERFORM discover_invitation_recipient_authority_scopes(NULL,bad_limit);
  EXCEPTION WHEN invalid_parameter_value THEN refused:=true; END;
  IF NOT refused THEN RAISE EXCEPTION 'Server admitted an unbounded authority scope page'; END IF;
 END LOOP;
 FOREACH bad_worker IN ARRAY ARRAY[NULL::uuid,'00000000-0000-0000-0000-000000000000'::uuid] LOOP
  refused:=false;
  BEGIN PERFORM claim_invitation_recipient_authority_job(bad_worker);
  EXCEPTION WHEN invalid_parameter_value THEN refused:=true; END;
  IF NOT refused THEN RAISE EXCEPTION 'Server admitted an unidentified authority Worker'; END IF;
 END LOOP;
 RESET ROLE;
 SET LOCAL ROLE strataai_api_runtime;
 refused:=false;
 BEGIN PERFORM discover_invitation_recipient_authority_scopes(NULL,100);
 EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API executed global authority discovery'; END IF;
 refused:=false;
 BEGIN PERFORM claim_invitation_recipient_authority_job(gen_random_uuid());
 EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API executed authority claims'; END IF;
 RESET ROLE;
 RAISE NOTICE 'Authority discovery: direct server limits, required Worker identity and API capability refusal passed';
END $$;
ROLLBACK;
