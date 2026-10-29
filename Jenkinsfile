// =============================================================
// FlashStore - PIPELINE 2: validación de seguridad y decisión GO/NO-GO
//
// Se ejecuta contra la versión desplegada por el pipeline 1 en Google Cloud
// (lee la IP del estado remoto de Terraform). Recolecta TODOS los resultados
// y decide al final: si "report go-no-go --security" da NO-GO, el build falla.
//
//   1. Análisis estático en paralelo: gitleaks, checkov, dotnet --vulnerable, npm audit
//   2. Pruebas unitarias (CLI: test unit)
//   3. Imágenes Docker con trivy
//   4. DAST con OWASP ZAP (PWA y API)
//   5. Pruebas funcionales en la nube: Insomnia (inso) y venta relámpago de 300 compradores
//   6. Compuerta de seguridad + reporte GO/NO-GO
// =============================================================

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
        timeout(time: 90, unit: 'MINUTES')
    }

    parameters {
        booleanParam(name: 'ESCANEAR_IMAGENES', defaultValue: true,
                     description: 'Construir las imágenes y analizarlas con trivy (tarda unos 10 min)')
        booleanParam(name: 'EJECUTAR_ZAP', defaultValue: true,
                     description: 'Escaneo pasivo con OWASP ZAP contra la PWA y la API desplegadas')
        string(name: 'COMPRADORES', defaultValue: '300',
               description: 'Compradores simultáneos en la venta relámpago (5 unidades en stock)')
    }

    environment {
        TF_DIR                         = 'infrastructure/terraform'
        TF_IN_AUTOMATION               = 'true'
        GOOGLE_APPLICATION_CREDENTIALS = credentials('gcp-terraform-sa')
        DEPLOY_KEY                     = credentials('flashstore-gcp-ssh')
        SECRETS_FILE                   = credentials('flashstore-secrets')
        DOTNET_CLI_TELEMETRY_OPTOUT    = '1'
        DOTNET_NOLOGO                  = '1'
        SEC                            = 'reports/security'
        // Versiones fijas de las herramientas (contenedores)
        GITLEAKS_IMAGE                 = 'zricethezav/gitleaks:v8.21.2'
        CHECKOV_IMAGE                  = 'bridgecrew/checkov:3.2.255'
        TRIVY_IMAGE                    = 'aquasec/trivy:0.56.2'
        ZAP_IMAGE                      = 'ghcr.io/zaproxy/zaproxy:2.16.0'
    }

    stages {
        stage('Preparar') {
            steps {
                sh '''
                    scripts/check-clock.sh || [ $? -eq 2 ]
                    rm -rf reports/security reports/cli
                    mkdir -p "$SEC/zap" "$SEC/checkov" reports/cli "$HOME/.ssh"
                    chmod 777 "$SEC/zap"
                    terraform -chdir="$TF_DIR" init -input=false -backend-config=backend.hcl > /dev/null
                '''
                script {
                    def ip = sh(script: 'terraform -chdir="$TF_DIR" output -raw public_ip 2>/dev/null || true', returnStdout: true).trim()
                    if (!(ip ==~ /\d{1,3}(\.\d{1,3}){3}/)) {
                        error('No hay infraestructura desplegada. Ejecute primero el pipeline 1 (flashstore-infra) con ACCION=crear.')
                    }
                    env.TARGET_IP   = ip
                    env.API_URL     = "http://${ip}:5000"
                    env.WEB_URL     = "http://${ip}:3000"
                    env.KNOWN_HOSTS = sh(script: 'echo "$HOME/.ssh/known_hosts_flashstore_gcp"', returnStdout: true).trim()
                    // Variables que usa el CLI en todas las etapas
                    env.FLASHSTORE_API      = env.API_URL
                    env.FLASHSTORE_SSH      = "${env.DEPLOY_KEY_USR}@${ip}:22"
                    env.FLASHSTORE_SSH_OPTS = "-i ${env.DEPLOY_KEY} -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o UserKnownHostsFile=${env.KNOWN_HOSTS}"
                    currentBuild.description = "Objetivo: ${ip}"
                }
                sh 'dotnet run --project cli/FlashStore.Cli -- health'
            }
        }

        stage('Análisis estático') {
            parallel {
                stage('Secretos (gitleaks)') {
                    steps {
                        sh '''
                            docker run --rm -u "$(id -u):$(id -g)" -v "$WORKSPACE:/repo" "$GITLEAKS_IMAGE" \
                                detect --source=/repo --redact --no-banner --exit-code 0 \
                                --report-format=json --report-path=/repo/$SEC/gitleaks.json
                        '''
                    }
                }
                stage('Terraform (checkov)') {
                    steps {
                        sh '''
                            docker run --rm -v "$WORKSPACE/$TF_DIR:/tf:ro" "$CHECKOV_IMAGE" \
                                -d /tf --framework terraform --compact --quiet -o json --soft-fail \
                                > "$SEC/checkov/results_json.json"
                        '''
                    }
                }
                stage('Dependencias .NET') {
                    steps {
                        sh '''
                            dotnet restore FlashStore.sln > /dev/null
                            dotnet list FlashStore.sln package --vulnerable --include-transitive --format json \
                                > "$SEC/dotnet-vulnerable.json"
                        '''
                    }
                }
                stage('Dependencias PWA (npm audit)') {
                    steps {
                        sh '''
                            cd frontend
                            npm audit --omit=dev --json > "../$SEC/npm-audit.json" || true
                        '''
                    }
                }
            }
        }

        stage('Pruebas unitarias') {
            steps {
                sh 'dotnet run --project cli/FlashStore.Cli -- test unit || true'
            }
        }

        stage('Imágenes (trivy)') {
            when { expression { params.ESCANEAR_IMAGENES } }
            steps {
                sh '''
                    set -a; . "$SECRETS_FILE"; set +a
                    export POSTGRES_PASSWORD="$FLASHSTORE_POSTGRES_PASSWORD" RABBITMQ_PASSWORD="$FLASHSTORE_RABBITMQ_PASSWORD" JWT_KEY="$FLASHSTORE_JWT_KEY"
                    docker compose -p flashstore-ci build --pull api worker web
                    for svc in api worker web; do
                        docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v flashstore-trivy-cache:/root/.cache \
                            "$TRIVY_IMAGE" image --quiet --scanners vuln --severity HIGH,CRITICAL --ignore-unfixed \
                            --format json "flashstore-$svc:local" > "$SEC/trivy-$svc.json"
                    done
                '''
            }
        }

        stage('DAST (OWASP ZAP)') {
            when { expression { params.EJECUTAR_ZAP } }
            steps {
                sh '''
                    docker run --rm -v "$WORKSPACE/$SEC/zap:/zap/wrk:rw" "$ZAP_IMAGE" \
                        zap-baseline.py -t "$WEB_URL" -m 2 -I -J zap-pwa.json -r zap-pwa.html || true
                    docker run --rm -v "$WORKSPACE/$SEC/zap:/zap/wrk:rw" "$ZAP_IMAGE" \
                        zap-api-scan.py -t "$API_URL/swagger/v1/swagger.json" -f openapi -S -I -J zap-api.json -r zap-api.html || true
                '''
            }
        }

        stage('Pruebas funcionales en la nube') {
            steps {
                sh '''
                    CLI="dotnet run --project cli/FlashStore.Cli --"
                    $CLI test api || true
                    $CLI db reset-flash-sale --yes
                    $CLI sale simulate --buyers "$COMPRADORES" || true
                    $CLI queue dlq || true
                '''
            }
        }

        stage('Compuerta de seguridad') {
            steps {
                sh 'python3 scripts/security_gate.py --dir "$SEC" --out reports/cli'
            }
        }

        stage('Decisión GO/NO-GO') {
            steps {
                // Código 1 = NO-GO: el build falla y la versión no se promueve.
                sh 'dotnet run --project cli/FlashStore.Cli -- report go-no-go --security'
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'reports/go-no-go.*, reports/cli/*.json, reports/security/**, reports/unit/*.trx',
                             allowEmptyArchive: true
            publishHTML(target: [reportName: 'OWASP ZAP - PWA', reportDir: 'reports/security/zap',
                                 reportFiles: 'zap-pwa.html', keepAll: true, alwaysLinkToLastBuild: true, allowMissing: true])
            publishHTML(target: [reportName: 'OWASP ZAP - API', reportDir: 'reports/security/zap',
                                 reportFiles: 'zap-api.html', keepAll: true, alwaysLinkToLastBuild: true, allowMissing: true])
        }
        success {
            echo "GO: la versión desplegada en ${env.TARGET_IP} cumple los criterios de salida."
        }
        failure {
            echo 'NO-GO o error: revise reports/go-no-go.md y reports/security/resumen.md en los artefactos.'
        }
    }
}
