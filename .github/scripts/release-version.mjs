// Works out the version a publish run releases, from the repository's existing v* tags and the
// bump the person releasing chose. Used by .github/workflows/publish.yml; see docs/releasing.md.
//
// Run directly: node release-version.mjs <bump> [prerelease] < tags-one-per-line
// Prints the version without the v prefix, e.g. 1.0.9 or 1.1.0-rc.1.

const BUMPS = ['patch', 'minor', 'major'];
const STABLE_TAG = /^v(\d+)\.(\d+)\.(\d+)$/;
// SemVer prerelease: dot-separated identifiers; a purely numeric one may not have a leading zero.
const PRERELEASE_IDENTIFIER = /^(0|[1-9]\d*|\d*[A-Za-z-][0-9A-Za-z-]*)$/;

/**
 * @param {string[]} tags Every tag in the repository.
 * @param {'patch' | 'minor' | 'major'} bump
 * @param {string} [prerelease] A SemVer prerelease label such as rc.1, or empty for a stable release.
 * @returns {string} The next version, without the v prefix.
 */
export function nextVersion(tags, bump, prerelease = '') {
    if (!BUMPS.includes(bump)) {
        throw new Error(`bump must be one of ${BUMPS.join(', ')}; got '${bump}'.`);
    }

    if (prerelease !== '' && !prerelease.split('.').every((identifier) => PRERELEASE_IDENTIFIER.test(identifier))) {
        throw new Error(`prerelease must be a SemVer label such as rc.1; got '${prerelease}'.`);
    }

    // Only stable tags are a base: v1.1.0-rc.1 followed by a minor bump is still 1.1.0, not 1.2.0.
    const [major, minor, patch] = tags
        .map((tag) => STABLE_TAG.exec(tag.trim()))
        .filter((match) => match !== null)
        .map((match) => match.slice(1, 4).map(Number))
        .reduce((latest, current) => (compare(current, latest) > 0 ? current : latest), [0, 0, 0]);

    const core =
        bump === 'major' ? `${major + 1}.0.0` :
        bump === 'minor' ? `${major}.${minor + 1}.0` :
        `${major}.${minor}.${patch + 1}`;

    const version = prerelease === '' ? core : `${core}-${prerelease}`;

    if (tags.some((tag) => tag.trim() === `v${version}`)) {
        throw new Error(`v${version} already exists. Choose a different bump or prerelease label.`);
    }

    return version;
}

function compare(left, right) {
    for (let index = 0; index < 3; index++) {
        if (left[index] !== right[index]) {
            return left[index] - right[index];
        }
    }

    return 0;
}

// Only when run as a script, not when the tests import it.
if (process.argv[1]?.endsWith('release-version.mjs')) {
    const [bump, prerelease = ''] = process.argv.slice(2);
    const input = await new Promise((resolve) => {
        let data = '';
        process.stdin.on('data', (chunk) => (data += chunk));
        process.stdin.on('end', () => resolve(data));
    });

    process.stdout.write(nextVersion(input.split(/\r?\n/).filter(Boolean), bump, prerelease));
}
