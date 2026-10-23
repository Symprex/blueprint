import assert from 'node:assert/strict';
import { test } from 'node:test';
import { MANAGED_LABELS, parsePrTitle } from './pr-title.mjs';

test('a fix is labelled bug', () => {
    assert.deepEqual(parsePrTitle('fix(validation): accept trailing-dot FQDNs').labels, ['bug']);
});

test('a feature is labelled enhancement', () => {
    assert.deepEqual(parsePrTitle('feat(openapi): emit nullable item schemas').labels, ['enhancement']);
});

test('a scope is optional', () => {
    assert.equal(parsePrTitle('docs: describe the release flow').valid, true);
});

test('docs are labelled documentation', () => {
    assert.deepEqual(parsePrTitle('docs: describe the release flow').labels, ['documentation']);
});

test('build, ci, chore, test and refactor are labelled maintenance', () => {
    for (const type of ['build', 'ci', 'chore', 'test', 'refactor']) {
        assert.deepEqual(parsePrTitle(`${type}: tidy`).labels, ['maintenance'], type);
    }
});

test('a ! marks the change breaking as well as typed', () => {
    assert.deepEqual(parsePrTitle('feat(runtime)!: drop the legacy pipeline').labels.sort(), ['breaking', 'enhancement']);
});

test('the bracketed [Area] style is rejected', () => {
    assert.equal(parsePrTitle('[Rebus] Let a plain handler defer').valid, false);
});

test('an unknown type is rejected', () => {
    assert.equal(parsePrTitle('feature: add a thing').valid, false);
});

test('a missing description is rejected', () => {
    assert.equal(parsePrTitle('fix(runtime): ').valid, false);
});

test('a capitalised type is rejected', () => {
    assert.equal(parsePrTitle('Fix: correct a thing').valid, false);
});

test('every label it applies is one it manages, so a retitle can remove stale ones', () => {
    for (const type of ['feat', 'fix', 'perf', 'docs', 'build', 'ci', 'chore', 'test', 'refactor', 'revert']) {
        for (const label of parsePrTitle(`${type}!: x`).labels) {
            assert.ok(MANAGED_LABELS.includes(label), label);
        }
    }
});
