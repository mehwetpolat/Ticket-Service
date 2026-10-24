$ErrorActionPreference = 'Stop'

$url = 'http://localhost:8787'
$healthUrl = "$url/api/health"

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker bulunamadi. Once Docker Desktop kurun ve acin: https://www.docker.com/products/docker-desktop/'
    }

    docker info --format '{{.ServerVersion}}' *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker calismiyor. Docker Desktop uygulamasini acin, hazir olmasini bekleyin ve yeniden deneyin.'
    }

    docker compose version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Compose bulunamadi. Docker Desktop kurulumunu onarip yeniden deneyin.'
    }

    Write-Host 'TickeTask baslatiliyor; imaj guncelleniyor (onbellek sayesinde genellikle kisa surer)...'
    docker compose up -d --build --remove-orphans --pull missing
    if ($LASTEXITCODE -ne 0) {
        throw 'Baslatma tamamlanamadi. Yukaridaki Docker hatasini kontrol edin.'
    }

    Write-Host 'Uygulamanin hazir olmasi bekleniyor...'
    $ready = $false
    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUrl -TimeoutSec 3
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        } catch {
            # Container is still starting; keep waiting until the deadline.
        }
        Start-Sleep -Seconds 2
    }

    if (-not $ready) {
        Write-Host 'Konteynerler calisiyor ancak uygulama henuz yanit vermiyor.' -ForegroundColor Yellow
        Write-Host 'Durumu kontrol edin: docker compose ps'
        Write-Host 'Ayrintili log: docker compose logs -f app'
        exit 1
    }

    $containerId = (docker compose ps -q app 2>$null | Select-Object -First 1)
    $secretPath = $null
    if (-not [string]::IsNullOrWhiteSpace($containerId)) {
        try {
            $mounts = (docker inspect $containerId --format '{{json .Mounts}}' 2>$null | ConvertFrom-Json)
            $secretDirectory = ($mounts | Where-Object { $_.Destination -eq '/app/.ticketask-runtime' } | Select-Object -First 1).Source
            if (-not [string]::IsNullOrWhiteSpace($secretDirectory)) {
                $secretPath = Join-Path $secretDirectory 'admin-password'
            }
        }
        catch {
            $secretPath = $null
        }
    }
    if ([string]::IsNullOrWhiteSpace($secretPath)) {
        $secretPath = Join-Path (Get-Location) 'data\secrets\admin-password'
    }

    Write-Host ''
    Write-Host 'TickeTask hazir: http://localhost:8787'
    Write-Host 'Varsayilan olarak yalnizca bu bilgisayardan erisilebilir.'
    Write-Host 'Sirket agina acmak icin .env dosyasinda TICKETASK_BIND_ADDRESS=0.0.0.0 ayarlayin.'
    Write-Host "Ilk giris sifresi: $secretPath"
    Write-Host 'Dosyadaki degerin tamamini kopyalayin; basinda veya sonunda bosluk birakmayin.'
    Write-Host 'Kurulum verileri sifirlanirsa bu dosya yeniden olusturulur ve eski sifre gecersiz olur.'
    Write-Host 'Sifreyi kimseyle paylasmayin; giris yaptiktan sonra Ayarlar > Sifre ve erisim bolumunden degistirin.'
    Start-Process $url
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
