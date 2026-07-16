CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE EXTENSION IF NOT EXISTS citext;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        email citext NOT NULL,
        password_hash text,
        full_name character varying(160) NOT NULL,
        email_verified_at timestamptz,
        mfa_enabled boolean NOT NULL,
        created_at timestamptz NOT NULL,
        last_login_at timestamptz,
        CONSTRAINT pk_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE action_recipients (
        id uuid NOT NULL,
        action_id uuid NOT NULL,
        name character varying(180) NOT NULL,
        email citext NOT NULL,
        phone character varying(40),
        status smallint NOT NULL,
        access_token_hash bytea NOT NULL,
        token_expires_at timestamptz,
        otp_required boolean NOT NULL,
        first_viewed_at timestamptz,
        started_at timestamptz,
        last_activity_at timestamptz,
        submitted_at timestamptz,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_action_recipients PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE reminder_schedules (
        id uuid NOT NULL,
        action_recipient_id uuid NOT NULL,
        trigger_at timestamptz NOT NULL,
        type smallint NOT NULL,
        status smallint NOT NULL,
        attempt_count integer NOT NULL,
        sent_at timestamptz,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_reminder_schedules PRIMARY KEY (id),
        CONSTRAINT fk_reminder_schedules_action_recipients_action_recipient_id FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE actions (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        template_id uuid,
        template_version_id uuid,
        public_reference character varying(32) NOT NULL,
        title character varying(240) NOT NULL,
        description text,
        status smallint NOT NULL,
        due_at timestamptz,
        expires_at timestamptz,
        sent_at timestamptz,
        completed_at timestamptz,
        created_by_user_id uuid NOT NULL,
        settings_json jsonb NOT NULL,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        cancelled_at timestamptz,
        deleted_at timestamptz,
        CONSTRAINT pk_actions PRIMARY KEY (id),
        CONSTRAINT ck_actions_due_after_created CHECK (due_at IS NULL OR due_at >= created_at),
        CONSTRAINT fk_actions_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE submissions (
        id uuid NOT NULL,
        action_id uuid NOT NULL,
        action_recipient_id uuid NOT NULL,
        version_number integer NOT NULL,
        status smallint NOT NULL,
        submitted_at timestamptz NOT NULL,
        reviewed_at timestamptz,
        reviewed_by_user_id uuid,
        review_comment text,
        content_hash bytea NOT NULL,
        CONSTRAINT pk_submissions PRIMARY KEY (id),
        CONSTRAINT fk_submissions_action_recipients_action_recipient_id FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE RESTRICT,
        CONSTRAINT fk_submissions_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE CASCADE,
        CONSTRAINT fk_submissions_users_reviewed_by_user_id FOREIGN KEY (reviewed_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE admin_settings (
        id uuid NOT NULL,
        organization_id uuid,
        scope smallint NOT NULL,
        category character varying(120) NOT NULL,
        key character varying(160) NOT NULL,
        value_json jsonb NOT NULL,
        is_secret boolean NOT NULL,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        updated_by_user_id uuid,
        CONSTRAINT pk_admin_settings PRIMARY KEY (id),
        CONSTRAINT fk_admin_settings_users_updated_by_user_id FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE api_keys (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        name character varying(120) NOT NULL,
        key_prefix character varying(16) NOT NULL,
        secret_hash bytea NOT NULL,
        scopes text[] NOT NULL,
        last_used_at timestamptz,
        expires_at timestamptz,
        revoked_at timestamptz,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_api_keys PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE audit_events (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        action_id uuid,
        actor_type smallint NOT NULL,
        actor_id character varying(100),
        event_type character varying(100) NOT NULL,
        event_data jsonb NOT NULL,
        ip_address inet,
        user_agent text,
        correlation_id uuid,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_audit_events PRIMARY KEY (id),
        CONSTRAINT fk_audit_events_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE draft_responses (
        id uuid NOT NULL,
        action_recipient_id uuid NOT NULL,
        requirement_id uuid NOT NULL,
        value_json jsonb,
        version integer NOT NULL,
        updated_by_session uuid,
        updated_at timestamptz NOT NULL,
        CONSTRAINT pk_draft_responses PRIMARY KEY (id),
        CONSTRAINT fk_draft_responses_action_recipients_action_recipient_id FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE file_assets (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        action_id uuid,
        action_recipient_id uuid,
        storage_key character varying(500) NOT NULL,
        original_file_name character varying(255) NOT NULL,
        mime_type character varying(160) NOT NULL,
        extension character varying(20),
        size_bytes bigint NOT NULL,
        sha256hash bytea,
        scan_status smallint NOT NULL,
        scan_engine character varying(100),
        scan_completed_at timestamptz,
        retention_until timestamptz,
        created_at timestamptz NOT NULL,
        deleted_at timestamptz,
        CONSTRAINT pk_file_assets PRIMARY KEY (id),
        CONSTRAINT fk_file_assets_action_recipients_action_recipient_id FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE RESTRICT,
        CONSTRAINT fk_file_assets_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE organizations (
        id uuid NOT NULL,
        name character varying(200) NOT NULL,
        slug character varying(100) NOT NULL,
        status smallint NOT NULL,
        timezone character varying(64) NOT NULL,
        default_language character varying(12) NOT NULL,
        logo_file_id uuid,
        accent_color character varying(7),
        privacy_statement text,
        retention_days integer NOT NULL,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        deleted_at timestamptz,
        CONSTRAINT pk_organizations PRIMARY KEY (id),
        CONSTRAINT fk_organizations_file_assets_logo_file_id FOREIGN KEY (logo_file_id) REFERENCES file_assets (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE notification_deliveries (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        action_recipient_id uuid,
        channel smallint NOT NULL,
        template_key character varying(100) NOT NULL,
        provider_message_id character varying(200),
        status smallint NOT NULL,
        error_code character varying(100),
        sent_at timestamptz,
        delivered_at timestamptz,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_notification_deliveries PRIMARY KEY (id),
        CONSTRAINT fk_notification_deliveries_action_recipients_action_recipient_ FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE SET NULL,
        CONSTRAINT fk_notification_deliveries_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE organization_users (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        user_id uuid NOT NULL,
        role smallint NOT NULL,
        status smallint NOT NULL,
        invited_at timestamptz,
        joined_at timestamptz,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_organization_users PRIMARY KEY (id),
        CONSTRAINT fk_organization_users_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT,
        CONSTRAINT fk_organization_users_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE usage_events (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        action_id uuid,
        event_type character varying(80) NOT NULL,
        quantity numeric(18,4) NOT NULL,
        unit character varying(32) NOT NULL,
        idempotency_key character varying(120) NOT NULL,
        occurred_at timestamptz NOT NULL,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_usage_events PRIMARY KEY (id),
        CONSTRAINT fk_usage_events_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE SET NULL,
        CONSTRAINT fk_usage_events_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE webhook_endpoints (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        url text NOT NULL,
        secret_ciphertext bytea NOT NULL,
        event_types text[] NOT NULL,
        status smallint NOT NULL,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_webhook_endpoints PRIMARY KEY (id),
        CONSTRAINT fk_webhook_endpoints_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE webhook_deliveries (
        id uuid NOT NULL,
        webhook_endpoint_id uuid NOT NULL,
        event_id uuid NOT NULL,
        attempt_number integer NOT NULL,
        status smallint NOT NULL,
        response_status integer,
        response_excerpt text,
        next_attempt_at timestamptz,
        created_at timestamptz NOT NULL,
        completed_at timestamptz,
        CONSTRAINT pk_webhook_deliveries PRIMARY KEY (id),
        CONSTRAINT fk_webhook_deliveries_webhook_endpoints_webhook_endpoint_id FOREIGN KEY (webhook_endpoint_id) REFERENCES webhook_endpoints (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE requirements (
        id uuid NOT NULL,
        action_id uuid NOT NULL,
        source_template_requirement_id uuid,
        key character varying(100) NOT NULL,
        type smallint NOT NULL,
        label character varying(300) NOT NULL,
        description text,
        is_required boolean NOT NULL,
        display_order integer NOT NULL,
        configuration_json jsonb NOT NULL,
        validation_json jsonb NOT NULL,
        condition_json jsonb,
        CONSTRAINT pk_requirements PRIMARY KEY (id),
        CONSTRAINT fk_requirements_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE submission_files (
        id uuid NOT NULL,
        submission_id uuid NOT NULL,
        requirement_id uuid NOT NULL,
        file_asset_id uuid NOT NULL,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_submission_files PRIMARY KEY (id),
        CONSTRAINT fk_submission_files_file_assets_file_asset_id FOREIGN KEY (file_asset_id) REFERENCES file_assets (id) ON DELETE RESTRICT,
        CONSTRAINT fk_submission_files_requirements_requirement_id FOREIGN KEY (requirement_id) REFERENCES requirements (id) ON DELETE RESTRICT,
        CONSTRAINT fk_submission_files_submissions_submission_id FOREIGN KEY (submission_id) REFERENCES submissions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE submission_responses (
        id uuid NOT NULL,
        submission_id uuid NOT NULL,
        requirement_id uuid NOT NULL,
        value_json jsonb,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_submission_responses PRIMARY KEY (id),
        CONSTRAINT fk_submission_responses_requirements_requirement_id FOREIGN KEY (requirement_id) REFERENCES requirements (id) ON DELETE RESTRICT,
        CONSTRAINT fk_submission_responses_submissions_submission_id FOREIGN KEY (submission_id) REFERENCES submissions (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE template_requirements (
        id uuid NOT NULL,
        template_version_id uuid NOT NULL,
        key character varying(100) NOT NULL,
        type smallint NOT NULL,
        label character varying(300) NOT NULL,
        description text,
        is_required boolean NOT NULL,
        display_order integer NOT NULL,
        configuration_json jsonb NOT NULL,
        validation_json jsonb NOT NULL,
        condition_json jsonb,
        CONSTRAINT pk_template_requirements PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE template_versions (
        id uuid NOT NULL,
        template_id uuid NOT NULL,
        version_number integer NOT NULL,
        title character varying(200) NOT NULL,
        instructions text,
        settings_json jsonb NOT NULL,
        published_at timestamptz,
        created_by_user_id uuid,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_template_versions PRIMARY KEY (id),
        CONSTRAINT fk_template_versions_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE TABLE templates (
        id uuid NOT NULL,
        organization_id uuid,
        name character varying(200) NOT NULL,
        category character varying(80),
        description text,
        status smallint NOT NULL,
        current_version_id uuid,
        created_by_user_id uuid,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        deleted_at timestamptz,
        CONSTRAINT pk_templates PRIMARY KEY (id),
        CONSTRAINT fk_templates_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT,
        CONSTRAINT fk_templates_template_versions_current_version_id FOREIGN KEY (current_version_id) REFERENCES template_versions (id) ON DELETE RESTRICT,
        CONSTRAINT fk_templates_users_created_by_user_id FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('25ff9a4f-61f3-4460-a92e-5b0784154b2f', 'security', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'recipientTokenDays', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '30');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('8c967ee1-9233-4edc-aa96-961ea6eca16e', 'retention', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'defaultRetentionDays', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '365');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('a573c60e-6d96-4204-a451-4c4c3f66ac31', 'files', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'maxUploadBytes', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '10485760');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('c17b8e86-e95f-4d19-a375-17672ce7c390', 'files', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'uploadUrlMinutes', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '10');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('cc13c863-f4e5-4c34-9029-9d77b82d713b', 'files', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'downloadUrlMinutes', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '5');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_recipients_action_status ON action_recipients (action_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX ix_recipients_token_hash ON action_recipients (access_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_actions_created_by_user_id ON actions (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_actions_org_due ON actions (organization_id, due_at) WHERE deleted_at IS NULL AND due_at IS NOT NULL AND status IN (2, 3);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_actions_org_status ON actions (organization_id, status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_actions_template_id ON actions (template_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_actions_template_version_id ON actions (template_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_actions_public_reference ON actions (public_reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_admin_settings_updated_by_user_id ON admin_settings (updated_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_admin_settings_global_category_key ON admin_settings (category, key) WHERE organization_id IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_admin_settings_org_category_key ON admin_settings (organization_id, category, key) WHERE organization_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_api_keys_org_prefix ON api_keys (organization_id, key_prefix);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_audit_action_created ON audit_events (action_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_audit_org_created ON audit_events (organization_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_draft_responses_requirement_id ON draft_responses (requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_draft_recipient_requirement ON draft_responses (action_recipient_id, requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_file_assets_action_id ON file_assets (action_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_file_assets_action_recipient_id ON file_assets (action_recipient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_files_org_created ON file_assets (organization_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_files_scan_status ON file_assets (scan_status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_file_assets_storage_key ON file_assets (storage_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_notification_deliveries_action_recipient_id ON notification_deliveries (action_recipient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_notification_deliveries_organization_id ON notification_deliveries (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_organization_users_user_id ON organization_users (user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_organization_users_org_user ON organization_users (organization_id, user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_organizations_logo_file_id ON organizations (logo_file_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_organizations_slug ON organizations (slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_reminder_schedules_action_recipient_id ON reminder_schedules (action_recipient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_reminders_due ON reminder_schedules (status, trigger_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_requirements_action_order ON requirements (action_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_requirements_source_template_requirement_id ON requirements (source_template_requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_requirements_action_key ON requirements (action_id, key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submission_files_file_asset_id ON submission_files (file_asset_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submission_files_requirement_id ON submission_files (requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submission_files_submission_id ON submission_files (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submission_responses_requirement_id ON submission_responses (requirement_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submission_responses_submission_id ON submission_responses (submission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submissions_action_recipient_id ON submissions (action_recipient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submissions_action_version ON submissions (action_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_submissions_reviewed_by_user_id ON submissions (reviewed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_template_requirements_version_order ON template_requirements (template_version_id, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_template_requirements_version_key ON template_requirements (template_version_id, key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_template_versions_created_by_user_id ON template_versions (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_template_versions_template_version ON template_versions (template_id, version_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_templates_created_by_user_id ON templates (created_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_templates_current_version_id ON templates (current_version_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_templates_org_name ON templates (organization_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_usage_events_action_id ON usage_events (action_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_usage_org_period ON usage_events (organization_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_usage_events_idempotency_key ON usage_events (idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE UNIQUE INDEX uq_users_email ON users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_webhook_deliveries_webhook_endpoint_id ON webhook_deliveries (webhook_endpoint_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_webhook_retry ON webhook_deliveries (status, next_attempt_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    CREATE INDEX ix_webhook_endpoints_organization_id ON webhook_endpoints (organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE action_recipients ADD CONSTRAINT fk_action_recipients_actions_action_id FOREIGN KEY (action_id) REFERENCES actions (id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE actions ADD CONSTRAINT fk_actions_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE actions ADD CONSTRAINT fk_actions_template_versions_template_version_id FOREIGN KEY (template_version_id) REFERENCES template_versions (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE actions ADD CONSTRAINT fk_actions_templates_template_id FOREIGN KEY (template_id) REFERENCES templates (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE admin_settings ADD CONSTRAINT fk_admin_settings_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE api_keys ADD CONSTRAINT fk_api_keys_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE audit_events ADD CONSTRAINT fk_audit_events_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE draft_responses ADD CONSTRAINT fk_draft_responses_requirements_requirement_id FOREIGN KEY (requirement_id) REFERENCES requirements (id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE file_assets ADD CONSTRAINT fk_file_assets_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE requirements ADD CONSTRAINT fk_requirements_template_requirements_source_template_requirem FOREIGN KEY (source_template_requirement_id) REFERENCES template_requirements (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE template_requirements ADD CONSTRAINT fk_template_requirements_template_versions_template_version_id FOREIGN KEY (template_version_id) REFERENCES template_versions (id) ON DELETE CASCADE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    ALTER TABLE template_versions ADD CONSTRAINT fk_template_versions_templates_template_id FOREIGN KEY (template_id) REFERENCES templates (id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715161027_InitialAtlasSchema') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715161027_InitialAtlasSchema', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    ALTER TABLE action_recipients ADD otp_attempt_count integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    ALTER TABLE action_recipients ADD otp_code_hash bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    ALTER TABLE action_recipients ADD otp_expires_at timestamptz;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    ALTER TABLE action_recipients ADD otp_verified_at timestamptz;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE TABLE idempotency_records (
        id uuid NOT NULL,
        organization_id uuid NOT NULL,
        key character varying(120) NOT NULL,
        request_hash character varying(128) NOT NULL,
        status_code integer NOT NULL,
        response_json jsonb NOT NULL,
        created_at timestamptz NOT NULL,
        expires_at timestamptz NOT NULL,
        CONSTRAINT pk_idempotency_records PRIMARY KEY (id),
        CONSTRAINT fk_idempotency_records_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE TABLE recipient_access_sessions (
        id uuid NOT NULL,
        action_recipient_id uuid NOT NULL,
        session_token_hash bytea NOT NULL,
        otp_verified boolean NOT NULL,
        created_at timestamptz NOT NULL,
        expires_at timestamptz NOT NULL,
        revoked_at timestamptz,
        last_seen_at timestamptz NOT NULL,
        CONSTRAINT pk_recipient_access_sessions PRIMARY KEY (id),
        CONSTRAINT fk_recipient_access_sessions_action_recipients_action_recipien FOREIGN KEY (action_recipient_id) REFERENCES action_recipients (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE INDEX ix_idempotency_records_expiry ON idempotency_records (expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE UNIQUE INDEX uq_idempotency_records_org_key ON idempotency_records (organization_id, key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE INDEX ix_recipient_access_sessions_recipient_expiry ON recipient_access_sessions (action_recipient_id, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    CREATE UNIQUE INDEX uq_recipient_access_sessions_token_hash ON recipient_access_sessions (session_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715164829_SecurityRecipientAndIdempotency') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715164829_SecurityRecipientAndIdempotency', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715170211_EmailResendOtpSettings') THEN
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES
        ('6505a485-c0d9-4d29-8f00-7509f8a4bfd9', 'Email:Resend', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'FromName', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"RyvePool"'),
        ('75fd9ff9-7be9-40a8-a30f-b24b201cdf49', 'email', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'provider', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"resend"'),
        ('8ddca1a6-85ca-4f55-802b-3506d681e4ed', 'security', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'otpMinutes', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '10'),
        ('b64009db-2539-4745-9a44-808105d10db3', 'Email:Resend', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'From', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"no-reply@ryverental.info"'),
        ('c64f642f-a12a-4138-a963-178e6f072ea5', 'Email', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'ReplyTo', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"support@ryvepool.com"')
    ON CONFLICT (category, key) WHERE organization_id IS NULL DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715170211_EmailResendOtpSettings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715170211_EmailResendOtpSettings', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715172049_FrontendChecklistInviteEmail') THEN
    UPDATE admin_settings
    SET value_json = '"Reqara"'
    WHERE id = '6505a485-c0d9-4d29-8f00-7509f8a4bfd9'
        AND organization_id IS NULL
        AND value_json = '"RyvePool"';

    UPDATE admin_settings
    SET value_json = '"requests@reqara.com"'
    WHERE id = 'b64009db-2539-4745-9a44-808105d10db3'
        AND organization_id IS NULL
        AND value_json = '"no-reply@ryverental.info"';

    UPDATE admin_settings
    SET value_json = '"support@reqara.com"'
    WHERE id = 'c64f642f-a12a-4138-a963-178e6f072ea5'
        AND organization_id IS NULL
        AND value_json = '"support@ryvepool.com"';

    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES
        ('2af49998-5f6e-4d9d-a36f-91e8d4b4f9f0', 'security', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'recipientTokenGraceDays', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '30'),
        ('3cfbb448-a95f-4d94-89b8-d15b81a8bace', 'app', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'baseUrl', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"https://reqara.com"')
    ON CONFLICT (category, key) WHERE organization_id IS NULL DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715172049_FrontendChecklistInviteEmail') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715172049_FrontendChecklistInviteEmail', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE TABLE platform_staff (
        id uuid NOT NULL,
        email citext NOT NULL,
        password_hash character varying(500),
        full_name character varying(160) NOT NULL,
        role smallint NOT NULL,
        status smallint NOT NULL,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        last_login_at timestamptz,
        disabled_at timestamptz,
        CONSTRAINT pk_platform_staff PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE TABLE platform_audit_events (
        id uuid NOT NULL,
        staff_id uuid,
        event_type character varying(120) NOT NULL,
        event_data jsonb NOT NULL,
        ip_address inet,
        user_agent text,
        correlation_id uuid,
        created_at timestamptz NOT NULL,
        CONSTRAINT pk_platform_audit_events PRIMARY KEY (id),
        CONSTRAINT fk_platform_audit_events_platform_staff_staff_id FOREIGN KEY (staff_id) REFERENCES platform_staff (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE TABLE platform_organization_interests (
        id uuid NOT NULL,
        organization_name character varying(200) NOT NULL,
        contact_name character varying(160) NOT NULL,
        contact_email citext NOT NULL,
        contact_phone character varying(40),
        source character varying(80),
        region character varying(80),
        expected_volume character varying(120),
        message character varying(4000),
        status smallint NOT NULL,
        assigned_staff_id uuid,
        approved_organization_id uuid,
        notes character varying(4000),
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        approved_at timestamptz,
        rejected_at timestamptz,
        CONSTRAINT pk_platform_organization_interests PRIMARY KEY (id),
        CONSTRAINT fk_platform_organization_interests_organizations_approved_orga FOREIGN KEY (approved_organization_id) REFERENCES organizations (id) ON DELETE SET NULL,
        CONSTRAINT fk_platform_organization_interests_platform_staff_assigned_sta FOREIGN KEY (assigned_staff_id) REFERENCES platform_staff (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE TABLE platform_revenue_events (
        id uuid NOT NULL,
        organization_id uuid,
        type smallint NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(3) NOT NULL,
        source character varying(80) NOT NULL,
        external_reference character varying(200),
        occurred_at timestamptz NOT NULL,
        period_start timestamptz,
        period_end timestamptz,
        metadata_json jsonb NOT NULL,
        recorded_by_staff_id uuid,
        created_at timestamptz NOT NULL,
        updated_at timestamptz NOT NULL,
        CONSTRAINT pk_platform_revenue_events PRIMARY KEY (id),
        CONSTRAINT fk_platform_revenue_events_organizations_organization_id FOREIGN KEY (organization_id) REFERENCES organizations (id) ON DELETE SET NULL,
        CONSTRAINT fk_platform_revenue_events_platform_staff_recorded_by_staff_id FOREIGN KEY (recorded_by_staff_id) REFERENCES platform_staff (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_audit_created ON platform_audit_events (created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_audit_staff_created ON platform_audit_events (staff_id, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_interests_contact_email ON platform_organization_interests (contact_email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_interests_status_created ON platform_organization_interests (status, created_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_organization_interests_approved_organization_id ON platform_organization_interests (approved_organization_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_organization_interests_assigned_staff_id ON platform_organization_interests (assigned_staff_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_revenue_events_recorded_by_staff_id ON platform_revenue_events (recorded_by_staff_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_revenue_external_reference ON platform_revenue_events (external_reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_revenue_occurred ON platform_revenue_events (occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE INDEX ix_platform_revenue_org_occurred ON platform_revenue_events (organization_id, occurred_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    CREATE UNIQUE INDEX uq_platform_staff_email ON platform_staff (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715182840_PlatformAdministration') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715182840_PlatformAdministration', '10.0.4');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715222135_DigitalOceanSpacesAdminSettings') THEN
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('1f56f6c0-d0b9-4a3d-88d9-9a736e9cf1ad', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'BucketName', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '""');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('55228888-0a9a-43c3-8760-387a48ad864e', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'ServiceUrl', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '""');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('a6783782-f779-42ce-afde-f2d857fc19af', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'Region', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"nyc3"');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('b99bd470-792f-4dfd-8e91-bcbdf566bd17', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'QuarantinePrefix', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '"quarantine"');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('c8bf7e7c-a030-4481-8ab2-10dac8984d92', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', TRUE, 'AccessKey', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '""');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('e3a282d6-c69d-40a3-a3f8-77528257be18', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', TRUE, 'SecretKey', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, '""');
    INSERT INTO admin_settings (id, category, created_at, is_secret, key, organization_id, scope, updated_at, updated_by_user_id, value_json)
    VALUES ('fb40c9fd-25de-4c6b-83eb-f249c5a64e8f', 'DigitalOceanSpaces', TIMESTAMPTZ '2026-07-15T00:00:00+00:00', FALSE, 'ForcePathStyle', NULL, 1, TIMESTAMPTZ '2026-07-15T00:00:00+00:00', NULL, 'false');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260715222135_DigitalOceanSpacesAdminSettings') THEN
    INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
    VALUES ('20260715222135_DigitalOceanSpacesAdminSettings', '10.0.4');
    END IF;
END $EF$;
COMMIT;

