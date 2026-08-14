// node --test runtime-response.test.mjs
// Isolates the 204/205/304 empty-body contract so a guest NoContent snapshot cannot trip
// workerd's "Response with null body status cannot have body" warning.
import assert from "node:assert/strict";
import { test } from "node:test";
import { toResponse } from "./runtime.mjs";

test("204, 205 and 304 pass a null body even when the snapshot carries an empty string", () => {
  for (const status of [204, 205, 304]) {
    const response = toResponse({ status, headersJson: "{}", body: "", bodyBytes: null });
    assert.equal(response.status, status);
    assert.equal(response.body, null);
  }
});

test("200 still uses the snapshot body", async () => {
  const response = toResponse({ status: 200, headersJson: "{}", body: "ok", bodyBytes: null });
  assert.equal(response.status, 200);
  assert.equal(await response.text(), "ok");
});
