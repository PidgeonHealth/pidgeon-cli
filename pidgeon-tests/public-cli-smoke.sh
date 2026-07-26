#!/usr/bin/env bash
# This Source Code Form is subject to the terms of the Mozilla Public
# License, v. 2.0. If a copy of the MPL was not distributed with this
# file, You can obtain one at https://mozilla.org/MPL/2.0/.

set -euo pipefail

cli="${1:?usage: public-cli-smoke.sh <pidgeon-binary> [expected-version]}"
expected_version="${2:-0.1.0-beta.2}"

if [[ ! -x "$cli" ]]; then
  echo "CLI is not executable: $cli" >&2
  exit 1
fi

scratch="$(mktemp -d "${TMPDIR:-/tmp}/pidgeon-cli-smoke.XXXXXX")"
cleanup() {
  case "$scratch" in
    "${TMPDIR:-/tmp}"/pidgeon-cli-smoke.*) rm -rf -- "$scratch" ;;
    *) echo "Refusing to clean unexpected scratch path: $scratch" >&2 ;;
  esac
}
trap cleanup EXIT

# Isolate session and package state from the runner account. HOME alone does not
# redirect Environment.SpecialFolder.LocalApplicationData on macOS.
export HOME="$scratch/home"
export PIDGEON_HOME="$scratch/pidgeon-home"
mkdir -p "$HOME"

version="$("$cli" --version)"
[[ "$version" == *"$expected_version"* ]]

help="$("$cli" --help)"
for command in artifacts capabilities completions data deident find generate lookup path run session validate; do
  grep -Eq "^[[:space:]]+$command([[:space:]]|$)" <<<"$help"
done

"$cli" generate 'ADT^A01' \
  --seed 1234 \
  --as-of 2026-01-01T12:00:00 \
  --no-session \
  --output "$scratch/generated.hl7" \
  --share "$scratch/generated.pidgeonrun"
grep -q '^MSH|' "$scratch/generated.hl7"
test -s "$scratch/generated.pidgeonrun"

"$cli" validate "$scratch/generated.hl7"

printf '%s\r%s\r' \
  'MSH|^~\&|SENDER|FAC|RECV|FAC|20260101120000||ADT^A01|MSG1|P|2.3' \
  'PID|1||MRN123^^^FAC^MR||DOE^JOHN||19800101|M' \
  > "$scratch/identified.hl7"
"$cli" deident "$scratch/identified.hl7" "$scratch/deidentified.hl7" --salt public-smoke
test -s "$scratch/deidentified.hl7"
if grep -q 'DOE^JOHN' "$scratch/deidentified.hl7"; then
  echo "de-identification left the planted patient name intact" >&2
  exit 1
fi

data_json="$("$cli" --output-format json data list)"
grep -q '"name": "fhir-r4-core"' <<<"$data_json"

artifacts_json="$("$cli" --output-format json artifacts search davinci --kind data-package --limit 10)"
grep -q '"kind": "data-package"' <<<"$artifacts_json"

lookup_json="$("$cli" lookup PID --format json)"
[[ "$lookup_json" == \{* ]]
grep -q '"path": "PID"' <<<"$lookup_json"

find_json="$("$cli" find patient --field --format json --limit 1)"
[[ "$find_json" == \[* ]]
grep -q '"Location"' <<<"$find_json"

path_result="$("$cli" path validate patient.mrn 'ADT^A01' 2>&1)"
grep -qi 'valid' <<<"$path_result"

"$cli" session create public-smoke
session_list="$("$cli" session list 2>&1)"
grep -q 'public-smoke' <<<"$session_list"
"$cli" session export "$scratch/session.json"
test -s "$scratch/session.json"

completions="$("$cli" completions bash)"
grep -q 'dotnet-suggest' <<<"$completions"

capabilities="$("$cli" --output-format json capabilities --standard fhir)"
grep -q '"standard": "fhir"' <<<"$capabilities"
grep -q '"level":' <<<"$capabilities"

"$cli" run show "$scratch/generated.pidgeonrun"
"$cli" run verify "$scratch/generated.pidgeonrun"
"$cli" run replay "$scratch/generated.pidgeonrun" --output "$scratch/replayed.hl7"
cmp "$scratch/generated.hl7" "$scratch/replayed.hl7"
"$cli" run fork "$scratch/generated.pidgeonrun" --set seed=5678 --output "$scratch/forked.pidgeonrun"
test -s "$scratch/forked.pidgeonrun"
set +e
diff_result="$("$cli" run diff "$scratch/generated.pidgeonrun" "$scratch/forked.pidgeonrun" 2>&1)"
diff_status=$?
set -e
[[ $diff_status -eq 1 ]]
grep -q 'Artifacts differ' <<<"$diff_result"
link="$("$cli" run link "$scratch/generated.pidgeonrun")"
grep -q 'r1\.' <<<"$link"

# Every community command needs an honest failure or unsupported-path probe as
# well as its successful path above. Keep these in the isolated installed-tool
# smoke, not a source-only unit test: later public trains must certify exactly
# the bytes a stranger installs. Some discovery commands intentionally return a
# successful empty result, while malformed requests and missing local artifacts
# return a typed nonzero failure.
expect_status_and_text() {
  local expected_status="$1"
  local expected_text="$2"
  shift 2
  local output
  local status
  set +e
  output="$("$cli" "$@" 2>&1)"
  status=$?
  set -e
  if [[ "$status" -ne "$expected_status" ]]; then
    echo "Expected '$*' to exit $expected_status, got $status" >&2
    echo "$output" >&2
    exit 1
  fi
  if ! grep -Fq "$expected_text" <<<"$output"; then
    echo "Expected '$*' output to contain '$expected_text'" >&2
    echo "$output" >&2
    exit 1
  fi
}

expect_status_and_text 0 'No artifacts found.' \
  artifacts search definitely-not-real --kind data-package
expect_status_and_text 0 'No matching capability cells.' \
  capabilities --standard definitely-not-real
expect_status_and_text 1 "Unsupported shell 'definitely-not-a-shell'" \
  completions definitely-not-a-shell
expect_status_and_text 1 "Unknown package: 'definitely-not-real'." \
  data remove definitely-not-real
expect_status_and_text 1 'Input path does not exist:' \
  deident "$scratch/definitely-not-real.hl7" "$scratch/deidentified-missing.hl7" --salt public-smoke
test ! -e "$scratch/deidentified-missing.hl7"
expect_status_and_text 0 'No results found.' \
  find definitely-not-real --field --format json --limit 1
expect_status_and_text 1 'Message type not recognized.' \
  generate DefinitelyNotARealType --no-session
expect_status_and_text 1 'Could not infer standard from path' \
  lookup definitely-not-real --format json
expect_status_and_text 1 'Invalid Path: definitely.not.real for ADT^A01' \
  path validate definitely.not.real 'ADT^A01'
expect_status_and_text 4 'Artifact not found:' \
  run show "$scratch/definitely-not-real.pidgeonrun"
expect_status_and_text 1 "Session 'definitely-not-real' not found" \
  session show definitely-not-real
expect_status_and_text 1 'File not found:' \
  validate "$scratch/definitely-not-real.hl7"

echo "public CLI smoke passed"
