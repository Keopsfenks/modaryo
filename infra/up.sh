#!/usr/bin/env bash
# Altyapıyı WSL'deki Docker'da başlatır ve tüm container'lar sağlıklı olana kadar bekler.
# WSL boşta kalınca kapanabildiği için test/çalıştırma öncesi güvenle tekrar çağrılabilir.
set -euo pipefail
COMPOSE="wsl docker compose -f /mnt/c/Users/Gurbuz/Documents/projects/modaryo/infra/docker-compose.yml"
# Tek seferlik s3-init (çıkış koduyla biter) "--wait" ile birlikte kullanılamaz; ayrı çalıştırılır.
MSYS_NO_PATHCONV=1 $COMPOSE up -d --wait postgres valkey mailpit s3 >/dev/null 2>&1
MSYS_NO_PATHCONV=1 $COMPOSE up --no-deps --exit-code-from s3-init s3-init >/dev/null 2>&1
echo "altyapı hazır"
