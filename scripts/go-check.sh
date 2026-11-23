#!/usr/bin/env bash
# go.work içindeki her Go modülü için gofmt, go vet, staticcheck ve go test çalıştırır.
# Herhangi biri başarısız olursa sıfırdan farklı kodla çıkar.
#
# Gerekenler: Go 1.27.4 ve staticcheck (sabitlenmiş sürüm):
#   go install honnef.co/go/tools/cmd/staticcheck@v0.8.2
set -euo pipefail

STATICCHECK_VERSION="v0.8.2"

# Go ve $HOME/go/bin PATH'te yoksa ekle (Git Bash / WSL).
for dir in "/c/Program Files/Go/bin" "/usr/local/go/bin" "$HOME/go/bin"; do
  [ -d "$dir" ] && PATH="$dir:$PATH"
done
export PATH

command -v go >/dev/null || { echo "HATA: go bulunamadı. Go 1.27.4 kurun." >&2; exit 1; }
command -v staticcheck >/dev/null || {
  echo "HATA: staticcheck bulunamadı. Kurmak için:" >&2
  echo "  go install honnef.co/go/tools/cmd/staticcheck@${STATICCHECK_VERSION}" >&2
  exit 1
}

cd "$(dirname "$0")/.."

mapfile -t modules < <(go list -m -f '{{.Dir}}')
[ "${#modules[@]}" -gt 0 ] || { echo "HATA: go.work içinde modül yok." >&2; exit 1; }

fail() { echo "BAŞARISIZ: $1 ($2)" >&2; exit 1; }

for mod in "${modules[@]}"; do
  echo "==> $mod"
  (
    cd "$mod"
    unformatted="$(gofmt -l .)"
    if [ -n "$unformatted" ]; then
      echo "gofmt gerektiren dosyalar:" >&2
      echo "$unformatted" >&2
      exit 1
    fi
  ) || fail "gofmt" "$mod"
  (cd "$mod" && go vet ./...) || fail "go vet" "$mod"
  (cd "$mod" && staticcheck ./...) || fail "staticcheck" "$mod"
  (cd "$mod" && go test ./...) || fail "go test" "$mod"
done

echo "go-check: TAMAM"
