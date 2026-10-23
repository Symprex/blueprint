// Validates a pull request title against the Conventional Commits format and derives the labels
// that group it in GitHub's generated release notes (.github/release.yml). Used by
// .github/workflows/pr-title.yml; the format is documented in docs/releasing.md.

const TITLE = /^(?<type>[a-z]+)(?:\((?<scope>[^()\s]+)\))?(?<breaking>!)?: (?<description>\S.*)$/;

const LABEL_BY_TYPE = {
    feat: 'enhancement',
    fix: 'bug',
    perf: 'enhancement',
    docs: 'documentation',
    build: 'maintenance',
    ci: 'maintenance',
    chore: 'maintenance',
    test: 'maintenance',
    refactor: 'maintenance',
    revert: 'maintenance',
};

/** Every label this check applies, so a retitled PR loses the labels its old title earned. */
export const MANAGED_LABELS = ['breaking', ...new Set(Object.values(LABEL_BY_TYPE))];

/**
 * @param {string} title
 * @returns {{ valid: boolean, labels: string[], error?: string }}
 */
export function parsePrTitle(title) {
    const match = TITLE.exec(title.trim());

    if (match === null || !Object.hasOwn(LABEL_BY_TYPE, match.groups.type)) {
        return {
            valid: false,
            labels: [],
            error:
                `'${title}' is not a Conventional Commits title. Use type(scope): description, ` +
                `where type is one of ${Object.keys(LABEL_BY_TYPE).join(', ')}, and add ! before ` +
                'the colon for a breaking change. See docs/releasing.md.',
        };
    }

    const labels = [LABEL_BY_TYPE[match.groups.type]];

    if (match.groups.breaking) {
        labels.push('breaking');
    }

    return { valid: true, labels };
}
