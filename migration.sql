CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;
CREATE TABLE users (
    id uuid NOT NULL,
    name character varying(255) NOT NULL,
    email character varying(256) NOT NULL,
    password_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id)
);

CREATE TABLE forms (
    id uuid NOT NULL,
    title character varying(255) NOT NULL,
    description character varying(1024) NOT NULL,
    created_by uuid NOT NULL,
    source_type integer NOT NULL,
    is_public boolean NOT NULL,
    expires_at timestamp with time zone,
    show_results_after_submit boolean NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_forms PRIMARY KEY (id),
    CONSTRAINT fk_forms_users_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE refresh_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token character varying(1024) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by_token character varying(1024),
    CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
    CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE TABLE form_questions (
    id uuid NOT NULL,
    form_id uuid NOT NULL,
    text character varying(1024) NOT NULL,
    type integer NOT NULL,
    "order" integer NOT NULL,
    is_required boolean NOT NULL,
    ai_generated boolean NOT NULL,
    points integer,
    correct_answer character varying(1024),
    CONSTRAINT pk_form_questions PRIMARY KEY (id),
    CONSTRAINT fk_form_questions_forms_form_id FOREIGN KEY (form_id) REFERENCES forms (id) ON DELETE CASCADE
);

CREATE TABLE submissions (
    id uuid NOT NULL,
    form_id uuid NOT NULL,
    user_id uuid,
    respondent_token uuid NOT NULL,
    ip_address character varying(45),
    submitted_at timestamp with time zone NOT NULL,
    score integer,
    CONSTRAINT pk_submissions PRIMARY KEY (id),
    CONSTRAINT fk_submissions_forms_form_id FOREIGN KEY (form_id) REFERENCES forms (id) ON DELETE CASCADE,
    CONSTRAINT fk_submissions_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE SET NULL
);

CREATE TABLE question_options (
    id uuid NOT NULL,
    question_id uuid NOT NULL,
    text character varying(1024),
    "order" integer NOT NULL,
    is_correct boolean,
    CONSTRAINT pk_question_options PRIMARY KEY (id),
    CONSTRAINT fk_question_options_form_questions_question_id FOREIGN KEY (question_id) REFERENCES form_questions (id) ON DELETE CASCADE
);

CREATE TABLE answers (
    id uuid NOT NULL,
    submission_id uuid NOT NULL,
    question_id uuid NOT NULL,
    text_value character varying(1024),
    numeric_value double precision,
    score integer,
    CONSTRAINT pk_answers PRIMARY KEY (id),
    CONSTRAINT fk_answers_form_questions_question_id FOREIGN KEY (question_id) REFERENCES form_questions (id) ON DELETE CASCADE,
    CONSTRAINT fk_answers_submissions_submission_id FOREIGN KEY (submission_id) REFERENCES submissions (id) ON DELETE CASCADE
);

CREATE TABLE answer_selected_options (
    answer_id uuid NOT NULL,
    option_id uuid NOT NULL,
    CONSTRAINT pk_answer_selected_options PRIMARY KEY (answer_id, option_id),
    CONSTRAINT fk_answer_selected_options_answers_answer_id FOREIGN KEY (answer_id) REFERENCES answers (id) ON DELETE CASCADE,
    CONSTRAINT fk_answer_selected_options_question_options_option_id FOREIGN KEY (option_id) REFERENCES question_options (id) ON DELETE RESTRICT
);

CREATE INDEX ix_answer_selected_options_option_id ON answer_selected_options (option_id);

CREATE INDEX ix_answers_question_id ON answers (question_id);

CREATE INDEX ix_answers_submission_id ON answers (submission_id);

CREATE INDEX ix_form_questions_form_id ON form_questions (form_id);

CREATE INDEX ix_forms_created_by ON forms (created_by);

CREATE INDEX ix_question_options_question_id ON question_options (question_id);

CREATE UNIQUE INDEX ix_refresh_tokens_token ON refresh_tokens (token);

CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);

CREATE UNIQUE INDEX ix_submissions_form_id_respondent_token_ip_address ON submissions (form_id, respondent_token, ip_address);

CREATE UNIQUE INDEX ix_submissions_form_id_user_id ON submissions (form_id, user_id) WHERE "user_id" IS NOT NULL;

CREATE INDEX ix_submissions_user_id ON submissions (user_id);

CREATE UNIQUE INDEX ix_users_email ON users (email);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260421150052_InitialCreate', '10.0.6');

COMMIT;

START TRANSACTION;
ALTER TABLE users ALTER COLUMN password_hash TYPE character varying(255);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260421172232_AddPasswordHashMaxLength', '10.0.6');

COMMIT;

START TRANSACTION;
ALTER TABLE users ADD confirmation_sent_at timestamp with time zone;

ALTER TABLE users ADD is_email_verified boolean NOT NULL DEFAULT FALSE;

ALTER TABLE users ADD pending_registration_expires_at timestamp with time zone;

ALTER TABLE users ADD verified_at timestamp with time zone;

ALTER TABLE forms ALTER COLUMN description DROP NOT NULL;

CREATE TABLE user_confirmation_tokens (
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    token_hash character varying(64) NOT NULL,
    purpose integer NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    used_at timestamp with time zone,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_user_confirmation_tokens PRIMARY KEY (id),
    CONSTRAINT fk_user_confirmation_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX ix_user_confirmation_tokens_token_hash ON user_confirmation_tokens (token_hash);

CREATE INDEX ix_user_confirmation_tokens_user_id ON user_confirmation_tokens (user_id);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260423131504_AddEmailVerification', '10.0.6');

COMMIT;

