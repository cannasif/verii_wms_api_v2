pipeline {
    agent any

    environment {
        PUBLIC_DIR = 'C:\\inetpub\\wwwroot\\wms2-api'
        APPPOOL = 'wms2-api'
        PROJECT = 'verii_wms_api_v2.csproj'
        PUBLISH_DIR = "${WORKSPACE}\\publish-output"
    }

    stages {
        stage('Checkout') {
            steps {
                git branch: 'main',
                    url: 'https://github.com/cannasif/verii_wms_api_v2.git'
            }
        }

        stage('Publish') {
            steps {
                bat '''
                if exist "%PUBLISH_DIR%" rmdir /S /Q "%PUBLISH_DIR%"
                dotnet publish "%PROJECT%" -c Release -o "%PUBLISH_DIR%"
                '''
            }
        }

        stage('Stop AppPool') {
            steps {
                powershell '''
                $pool = $env:APPPOOL
                $appcmd = "$env:windir\\system32\\inetsrv\\appcmd.exe"
                $state = & $appcmd list apppool "$pool" /text:state 2>$null

                if (-not $state) {
                    throw "IIS AppPool bulunamadı: $pool"
                }

                if ($state -ne "Stopped") {
                    & $appcmd stop apppool /apppool.name:$pool | Out-Null
                }

                $deadline = (Get-Date).AddSeconds(30)
                do {
                    Start-Sleep -Seconds 1
                    $state = & $appcmd list apppool "$pool" /text:state
                    if ((Get-Date) -gt $deadline) {
                        throw "AppPool 30 saniye içinde durmadı: $pool"
                    }
                } while ($state -ne "Stopped")
                '''
            }
        }

        stage('Deploy') {
            steps {
                bat '''
                if not exist "%PUBLIC_DIR%" mkdir "%PUBLIC_DIR%"
                xcopy "%PUBLISH_DIR%\\*" "%PUBLIC_DIR%\\" /E /I /Y
                '''
            }
        }

        stage('Configure Persistent Upload Storage') {
            steps {
                powershell '''
                $uploadRoot = Join-Path $env:PUBLIC_DIR 'wwwroot\\uploads\\stock-images'
                $appPoolIdentity = "IIS AppPool\\$env:APPPOOL"

                New-Item -ItemType Directory -Path $uploadRoot -Force | Out-Null
                & icacls.exe $uploadRoot /grant:r "${appPoolIdentity}:(OI)(CI)M" /T /C | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Stok görseli klasör yetkisi ayarlanamadı: $uploadRoot"
                }
                '''
            }
        }

        stage('Start AppPool') {
            steps {
                bat '%windir%\\system32\\inetsrv\\appcmd start apppool /apppool.name:"%APPPOOL%"'
            }
        }
    }

    post {
        always {
            powershell '''
            $pool = $env:APPPOOL
            $appcmd = "$env:windir\\system32\\inetsrv\\appcmd.exe"
            $state = & $appcmd list apppool "$pool" /text:state 2>$null
            if ($state -eq "Stopped") {
                & $appcmd start apppool /apppool.name:$pool | Out-Null
            }
            '''
        }
        failure {
            echo 'DEPLOYMENT FAILED'
        }
        success {
            echo 'DEPLOYMENT SUCCESSFUL'
        }
        cleanup {
            bat 'if exist "%PUBLISH_DIR%" rmdir /S /Q "%PUBLISH_DIR%"'
        }
    }
}
