#!/usr/bin/env bash
set -euo pipefail
# Exercise argument construction only. The actual exact-image refusal matrix
# remains a separate mandatory container job; these mocks never leave this
# subshell or replace provider/runtime acceptance.
(
  mkdir -p artifacts
  fixture_root="$(realpath artifacts)"
  repository_root="$(realpath .)"
  [[ "$fixture_root" == "$repository_root/"* ]] || exit 2
  mktemp() {
    local created
    created="$(command mktemp -d "$fixture_root/fixture-args.XXXXXX")"
    [[ "$(realpath "$created")" == "$fixture_root/fixture-args."* ]] || exit 2
    printf '%s\n' "$created"
  }
  timeout() { [ "$1" = 30 ] || return 2; shift; "$@"; }
  docker() {
    local key value
    local -A values=()
    [ "$1" = run ] && [ "$2" = --rm ] && [ "$3" = --network ] && [ "$4" = none ] || return 2
    shift 4
    while [ "$#" -gt 1 ]; do
      [ "$1" = -e ] && [[ "$2" == *=* ]] || return 2
      key="${2%%=*}"; value="${2#*=}"
      if [[ -v "values[$key]" ]]; then echo 'Duplicate environment entry rejected.'; return 2; fi
      values["$key"]="$value"; shift 2
    done
    [ "${values[AWS_EC2_METADATA_DISABLED]}" = true ] || return 2
    [ "${values[ConnectionStrings__Postgres]}" = 'Host=unused;Database=unused;Username=unused;Password=unused' ] || return 2
    case "$1" in fixture-api|fixture-worker) ;; *) return 2 ;; esac
    if [ "${values[STRATAAI_ATTACHMENT_STORAGE_ENABLED]}" = invalid ]; then echo 'Attachment storage enablement is invalid.'
    elif [ "${values[STRATAAI_RUNTIME_MODE]}" = demo ]; then echo 'Managed attachment storage requires Production mode.'
    elif [ "${values[STRATAAI_ATTACHMENT_S3_BUCKET]}" = unsafe/bucket ]; then echo 'A canonical managed storage bucket is required.'
    elif [ "${values[STRATAAI_ATTACHMENT_S3_OWNER]}" = 123 ]; then echo 'An expected managed storage owner is required.'
    elif [ "${values[STRATAAI_ATTACHMENT_S3_REGION]}" = custom-region ]; then echo 'A supported explicit attachment storage region is required.'
    elif [ "${values[STRATAAI_ATTACHMENT_MAX_BYTES]-}" = invalid ]; then echo 'Attachment upload size policy is invalid.'
    elif [ "${values[STRATAAI_ATTACHMENT_ALLOWED_TYPES]-}" = image/svg+xml ]; then echo 'Attachment upload policy is invalid.'
    elif [ "$1" = fixture-worker ] && [ "${values[STRATAAI_WORKER_ORGANIZATION_IDS]}" = '' ]; then echo 'Attachment scanning requires explicit Worker Organization scope.'
    elif [ "$1" = fixture-worker ] && [[ "${values[STRATAAI_ATTACHMENT_SCANNER_SOCKET]}" = relative.sock || "${values[STRATAAI_ATTACHMENT_SCANNER_SOCKET]}" = /run/../scanner.sock ]]; then echo 'An absolute local scanner socket path is required.'
    else echo 'Missing expected refusal override.'; return 2
    fi
    return 1
  }
  source scripts/ci/test-attachment-runtime-configuration.sh fixture-api fixture-worker
  [ "$case_number" = 17 ]
  echo 'All 17 configuration fixtures pass argument-only checks with unique environment entries.'
)
