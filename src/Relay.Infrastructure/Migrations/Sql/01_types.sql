-- outcome_polarity enum: good | bad | neutral (Requirements §Data-quality).
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'outcome_polarity') THEN
        CREATE TYPE outcome_polarity AS ENUM ('good', 'bad', 'neutral');
    END IF;
END
$$;
