-- One-time, non-destructive conversion of the earlier MVP schema.
-- Receipt caches contain the latest state of old matches, including matches
-- that were replaced by New Match. The old tables are retained as backups.
DO $migration$
BEGIN
    IF to_regclass('"Matches"') IS NULL AND to_regclass('"Receipts"') IS NULL
        AND to_regclass('"Results"') IS NULL THEN
        RETURN;
    END IF;
    IF to_regclass('"Matches"') IS NULL OR to_regclass('"Receipts"') IS NULL
        OR to_regclass('"Results"') IS NULL THEN
        RAISE EXCEPTION 'Incomplete legacy TacticSim schema; refusing a partial import';
    END IF;

    CREATE TEMP TABLE "TacticSimLegacyViews" ON COMMIT DROP AS
    SELECT DISTINCT ON (r."OwnerId", r."ResponseJson"::jsonb ->> 'matchId')
        r."OwnerId", r."ResponseJson"::jsonb AS state
    FROM "Receipts" r
    ORDER BY r."OwnerId", r."ResponseJson"::jsonb ->> 'matchId',
        (r."ResponseJson"::jsonb ->> 'version')::bigint DESC;

    INSERT INTO "GameSessions" ("OwnerId", "ActiveMatchId", "Revision")
    SELECT "OwnerId", "MatchId", "Version" FROM "Matches";

    INSERT INTO "GameMatches" ("Id", "OwnerId", "Version", "MapKey", "CurrentSide", "TurnNumber",
        "Winner", "CreatedAt", "UpdatedAt", "CompletedAt", "LastEventSequence")
    SELECT (v.state ->> 'matchId')::uuid, v."OwnerId", (v.state ->> 'version')::bigint,
        'crossing-v1', v.state ->> 'currentSide', (v.state ->> 'turnNumber')::integer,
        v.state ->> 'winner', COALESCE(r."CompletedAt", transaction_timestamp()),
        COALESCE(r."CompletedAt", transaction_timestamp()),
        CASE WHEN v.state ->> 'winner' IS NOT NULL THEN COALESCE(r."CompletedAt", transaction_timestamp()) END,
        jsonb_array_length(v.state -> 'history')
    FROM "TacticSimLegacyViews" v
    LEFT JOIN "Results" r ON r."MatchId" = (v.state ->> 'matchId')::uuid;

    INSERT INTO "MatchUnits" ("MatchId", "UnitId", "Type", "Side", "X", "Y", "Health", "HasMoved", "HasAttacked")
    SELECT (v.state ->> 'matchId')::uuid, (u.value ->> 'id')::integer, u.value ->> 'type',
        u.value ->> 'side', (u.value ->> 'x')::integer, (u.value ->> 'y')::integer,
        (u.value ->> 'health')::integer, (u.value ->> 'hasMoved')::boolean, (u.value ->> 'hasAttacked')::boolean
    FROM "TacticSimLegacyViews" v CROSS JOIN LATERAL jsonb_array_elements(v.state -> 'units') u;

    INSERT INTO "MatchCommands" ("OwnerId", "CommandId", "MatchId", "Kind", "ResultingMatchVersion",
        "AcceptedAt", "Fingerprint", "ResponseJson")
    SELECT r."OwnerId", r."CommandId", (r."ResponseJson"::jsonb ->> 'matchId')::uuid, 'Legacy',
        (r."ResponseJson"::jsonb ->> 'version')::bigint, transaction_timestamp(), r."Fingerprint",
        (r."ResponseJson"::jsonb || jsonb_build_object(
            'matchVersion', (r."ResponseJson"::jsonb ->> 'version')::bigint,
            'history', (SELECT jsonb_agg(e.value || jsonb_build_object('sequence', e.ordinality) ORDER BY e.ordinality)
                FROM jsonb_array_elements(r."ResponseJson"::jsonb -> 'history') WITH ORDINALITY e(value, ordinality))))::text
    FROM "Receipts" r;

    -- Original text events did not record structured action fields or command IDs.
    -- Preserve their exact order/text and mark them honestly as Legacy events.
    INSERT INTO "MatchEvents" ("MatchId", "Sequence", "Turn", "Side", "Kind", "Damage", "Destroyed", "Text", "OccurredAt")
    SELECT (v.state ->> 'matchId')::uuid, e.ordinality, (e.value ->> 'turn')::integer,
        e.value ->> 'side', 'Legacy', 0, false, e.value ->> 'text', transaction_timestamp()
    FROM "TacticSimLegacyViews" v
    CROSS JOIN LATERAL jsonb_array_elements(v.state -> 'history') WITH ORDINALITY e(value, ordinality);

    IF EXISTS (SELECT 1 FROM "GameSessions" s WHERE NOT EXISTS
        (SELECT 1 FROM "GameMatches" m WHERE m."Id" = s."ActiveMatchId" AND m."OwnerId" = s."OwnerId")) THEN
        RAISE EXCEPTION 'Legacy current match has no receipt snapshot; refusing incomplete conversion';
    END IF;
    IF EXISTS (SELECT 1 FROM "GameMatches" m WHERE
        (SELECT count(*) FROM "MatchUnits" u WHERE u."MatchId" = m."Id") <> 6) THEN
        RAISE EXCEPTION 'Legacy match has incomplete units; refusing incomplete conversion';
    END IF;
END $migration$;
