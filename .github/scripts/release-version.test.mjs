import assert from 'node:assert/strict';
import { test } from 'node:test';
import { nextVersion } from './release-version.mjs';

const tags = ['v1.0.7', 'v1.0.8-rc.1', 'v1.0.8-rc.2', 'v1.0.8', 'not-a-version'];

test('patch bumps the latest stable tag', () => {
    assert.equal(nextVersion(tags, 'patch'), '1.0.9');
});

test('minor bumps and resets the patch', () => {
    assert.equal(nextVersion(tags, 'minor'), '1.1.0');
});

test('major bumps and resets minor and patch', () => {
    assert.equal(nextVersion(tags, 'major'), '2.0.0');
});

test('a prerelease label is appended to the bumped version', () => {
    assert.equal(nextVersion(tags, 'minor', 'rc.1'), '1.1.0-rc.1');
});

test('prerelease tags never act as the base version', () => {
    assert.equal(nextVersion(['v1.0.8', 'v1.1.0-rc.1'], 'minor'), '1.1.0');
});

test('stable tags are compared numerically, not as text', () => {
    assert.equal(nextVersion(['v1.0.9', 'v1.0.10'], 'patch'), '1.0.11');
});

test('with no stable tag, the first release bumps from 0.0.0', () => {
    assert.equal(nextVersion([], 'minor'), '0.1.0');
});

test('refuses a version whose tag already exists', () => {
    assert.throws(() => nextVersion(['v1.0.8', 'v1.0.9-rc.1'], 'patch', 'rc.1'), /v1\.0\.9-rc\.1 already exists/);
});

test('refuses an unknown bump', () => {
    assert.throws(() => nextVersion(tags, 'huge'), /bump must be one of/);
});

test('refuses a malformed prerelease label', () => {
    assert.throws(() => nextVersion(tags, 'patch', 'rc 1'), /prerelease/);
});
