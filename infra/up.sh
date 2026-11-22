#!/usr/bin/env bash
# Altyapıyı WSL'deki Docker'da başlatır ve tüm container'lar sağlıklı olana kadar bekler.
# WSL boşta kalınca kapanabildiği için test/çalıştırma öncesi güvenle tekrar çağrılabilir.
set -euo pipefail
MSYS_NO_PATHCONV=1 wsl docker compose -f /mnt/c/Users/Gurbuz/Documents/projects/modaryo/infra/docker-compose.yml up -d --wait >/dev/null 2>&1
echo "altyapı hazır"
