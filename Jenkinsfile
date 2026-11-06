// =============================================================
// FlashStore - PIPELINE 2: validación de seguridad y decisión GO/NO-GO
//
// Se ejecuta contra la versión desplegada por el pipeline 1 en Google Cloud
// (lee la IP del estado remoto de Terraform). Recolecta TODOS los resultados
// y decide al final: si "report go-no-go --security" da NO-GO, el build falla.
//
//   1. Análisis estático en paralelo: ESLint, gitleaks, checkov, Semgrep (SAST, alternativa
//      a Fortify, D-20), dotnet --vulnerable, npm audit
//   2. Lint .NET (dotnet format), pruebas unitarias y SonarQube con Quality Gate
//   3. OWASP Dependency-Check (SCA)
//   4. Imágenes Docker con trivy
//   5. DAST con OWASP ZAP (PWA y API)
//   6. Pruebas funcionales en la nube: Insomnia (inso), Cypress E2E y venta relámpago
//   7. Rendimiento con k6: carga, estrés y pico
//   8. Compuertas de calidad y seguridad + reporte GO/NO-GO (--strict --security)
//
// Credenciales: gcp-terraform-sa, flashstore-gcp-ssh, flashstore-secrets,
//               sonar-token (Secret text), nvd-api-key (Secret text)
// =============================================================

pipeline {
    agent any

    options {
        timestamps()
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
        timeout(time: 150, unit: 'MINUTES')
    }

    parameters {
        booleanParam(name: 'ESCANEAR_IMAGENES', defaultValue: true,
                     description: 'Construir las imágenes y analizarlas con trivy (tarda unos 10 min)')
        booleanParam(name: 'EJECUTAR_ZAP', defaultValue: true,
                     description: 'Escaneo pasivo con OWASP ZAP contra la PWA y la API desplegadas')
        string(name: 'COMPRADORES', defaultValue: '300',
               description: 'Compradores simultáneos en la venta relámpago (5 unidades en stock)')
        booleanParam(name: 'EJECUTAR_DEPENDENCY_CHECK', defaultValue: true,
                     description: 'OWASP Dependency-Check (la primera vez descarga la base NVD: ~15 min con llave)')
        booleanParam(name: 'EJECUTAR_RENDIMIENTO', defaultValue: true,
                     description: 'k6: carga (2 min), estrés (~3 min) y pico (~1.5 min)')
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
        QUAL                           = 'reports/quality'
        SONAR_URL                      = 'http://localhost:9000'
        // Versiones fijas de las herramientas (contenedores)
        GITLEAKS_IMAGE                 = 'zricethezav/gitleaks:v8.21.2'
        CHECKOV_IMAGE                  = 'bridgecrew/checkov:3.2.255'
        TRIVY_IMAGE                    = 'aquasec/trivy:0.56.2'
        ZAP_IMAGE                      = 'ghcr.io/zaproxy/zaproxy:2.16.0'
        SEMGREP_IMAGE                  = 'semgrep/semgrep:1.95.0'
        DEPCHECK_IMAGE                 = 'owasp/dependency-check:12.1.0'
        CYPRESS_IMAGE                  = 'cypress/included:15.21.1'
        K6_IMAGE                       = 'grafana/k6:0.54.0'
    }

    stages {
        stage('Preparar') {
            steps {
                sh '''
                    scripts/check-clock.sh || [ $? -eq 2 ]
                    rm -rf reports/security reports/cli reports/quality reports/performance tests/cypress/results
                    mkdir -p "$SEC/zap" "$SEC/checkov" "$SEC/dependency-check" "$QUAL" reports/cli reports/performance "$HOME/.ssh"
                    chmod 777 "$SEC/zap" "$SEC/dependency-check" reports/performance
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
                stage('Lint PWA (ESLint)') {
                    steps {
                        sh '''
                            cd frontend
                            npm ci --no-audit --no-fund > /dev/null
                            npx eslint . -f json -o "../$QUAL/eslint.json" || true
                            npx eslint . || true
                        '''
                    }
                }
                stage('SAST (Semgrep)') {
                    steps {
                        sh '''
                            docker run --rm -u "$(id -u):$(id -g)" -e HOME=/tmp -v "$WORKSPACE:/src" "$SEMGREP_IMAGE" \
                                semgrep scan --metrics=off --quiet \
                                --config p/csharp --config p/javascript --config p/react \
                                --config p/dockerfile --config p/owasp-top-ten \
                                --exclude tests --exclude reports --exclude .tools --exclude "frontend/dist" \
                                --json --output /src/$SEC/semgrep.json /src || true
                        '''
                    }
                }
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

        stage('Lint .NET (dotnet format)') {
            steps {
                sh '''
                    set +e
                    dotnet format whitespace FlashStore.sln --verify-no-changes --verbosity minimal
                    echo $? > "$QUAL/dotnet-format.exit"
                    exit 0
                '''
            }
        }

        stage('Pruebas unitarias') {
            steps {
                sh 'dotnet run --project cli/FlashStore.Cli -- test unit || true'
            }
        }

        stage('Calidad (SonarQube)') {
            steps {
                withCredentials([string(credentialsId: 'sonar-token', variable: 'SONAR_TOKEN')]) {
                    sh '''
                        dotnet tool update --tool-path .tools dotnet-sonarscanner > /dev/null
                        .tools/dotnet-sonarscanner begin /k:flashstore /n:FlashStore /v:"$GIT_COMMIT" \
                            /d:sonar.host.url="$SONAR_URL" /d:sonar.token="$SONAR_TOKEN" \
                            /d:sonar.exclusions="**/bin/**,**/obj/**,**/node_modules/**,frontend/**,tests/cypress/**,reports/**" \
                            /d:sonar.qualitygate.wait=true /d:sonar.qualitygate.timeout=300
                        dotnet build FlashStore.sln -c Release --no-incremental > /dev/null
                        .tools/dotnet-sonarscanner end /d:sonar.token="$SONAR_TOKEN" || true
                        curl -s -u "$SONAR_TOKEN:" "$SONAR_URL/api/qualitygates/project_status?projectKey=flashstore" \
                            > "$QUAL/sonar-qualitygate.json"
                        cat "$QUAL/sonar-qualitygate.json"; echo
                    '''
                }
            }
        }

        stage('SCA (OWASP Dependency-Check)') {
            when { expression { params.EJECUTAR_DEPENDENCY_CHECK } }
            steps {
                withCredentials([string(credentialsId: 'nvd-api-key', variable: 'NVD_API_KEY')]) {
                    sh '''
                        set +x
                        echo "Dependency-Check: escaneando (la primera vez descarga la base NVD)..."
                        docker run --rm -v "$WORKSPACE:/src:ro" -v flashstore-depcheck-data:/usr/share/dependency-check/data \
                            -v "$WORKSPACE/$SEC/dependency-check:/report" "$DEPCHECK_IMAGE" \
                            --scan /src --project FlashStore --out /report --format JSON --format HTML \
                            --nvdApiKey "$NVD_API_KEY" --enableExperimental --disableAssembly \
                            --nodeAuditSkipDevDependencies --nodePackageSkipDevDependencies \
                            --exclude "**/node_modules/**" --exclude "**/bin/**" --exclude "**/obj/**" \
                            --exclude "**/reports/**" --exclude "**/.tools/**" --exclude "**/tests/cypress/**" || true
                    '''
                }
            }
        }

        stage('Imágenes (trivy)') {
            when { expression { params.ESCANEAR_IMAGENES } }
            steps {
                sh '''
                    # sh corre con -x (traza): se apaga para que los secretos no aparezcan en el log
                    set +x
                    set -a; . "$SECRETS_FILE"; set +a
                    export POSTGRES_PASSWORD="$FLASHSTORE_POSTGRES_PASSWORD" RABBITMQ_PASSWORD="$FLASHSTORE_RABBITMQ_PASSWORD" JWT_KEY="$FLASHSTORE_JWT_KEY"
                    set -x
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

                    # E2E con el stock reiniciado (las pruebas compran el producto 2, no la consola)
                    $CLI db reset-flash-sale --yes
                    set +e
                    docker run --rm -v "$WORKSPACE/tests/cypress:/e2e" -w /e2e -e CYPRESS_BASE_URL="$WEB_URL" \
                        "$CYPRESS_IMAGE" --browser electron \
                        --reporter junit --reporter-options "mochaFile=results/e2e-[hash].xml"
                    E2E=$?
                    docker run --rm -v "$WORKSPACE/tests/cypress:/e2e" --entrypoint sh "$CYPRESS_IMAGE" \
                        -c "chown -R $(id -u):$(id -g) /e2e"
                    set -e
                    python3 scripts/record_result.py test-e2e "$E2E" "Cypress E2E contra $WEB_URL"

                    # Venta relámpago con el stock reiniciado otra vez
                    $CLI db reset-flash-sale --yes
                    $CLI sale simulate --buyers "${COMPRADORES:-300}" || true
                    $CLI queue dlq || true
                '''
            }
        }

        stage('Rendimiento (k6)') {
            when { expression { params.EJECUTAR_RENDIMIENTO } }
            steps {
                sh '''
                    FALLAS=0
                    for ESC in carga estres pico; do
                        set +e
                        docker run --rm -u "$(id -u):$(id -g)" -v "$WORKSPACE:/w" -w /w "$K6_IMAGE" run --quiet \
                            -e BASE_URL="$API_URL" -e ESCENARIO=$ESC \
                            --summary-export=reports/performance/$ESC.json tests/performance/carga.js
                        CODE=$?
                        set -e
                        echo "Escenario $ESC: código $CODE"
                        [ $CODE -eq 0 ] || FALLAS=$((FALLAS+1))
                    done
                    python3 scripts/record_result.py test-load "$FALLAS" "k6 carga, estrés y pico: $FALLAS escenario(s) con umbrales incumplidos"
                '''
            }
        }

        stage('Compuerta de calidad') {
            steps {
                sh 'python3 scripts/quality_gate.py --dir "$QUAL" --out reports/cli'
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
                sh 'dotnet run --project cli/FlashStore.Cli -- report go-no-go --strict --security'
            }
        }
    }

    post {
        always {
            archiveArtifacts artifacts: 'reports/go-no-go.*, reports/cli/*.json, reports/security/**, reports/quality/**, reports/performance/*.json, reports/unit/*.trx, tests/cypress/screenshots/**',
                             allowEmptyArchive: true
            junit testResults: 'tests/cypress/results/*.xml', allowEmptyResults: true
            publishHTML(target: [reportName: 'OWASP Dependency-Check', reportDir: 'reports/security/dependency-check',
                                 reportFiles: 'dependency-check-report.html', keepAll: true, alwaysLinkToLastBuild: true, allowMissing: true])
            publishHTML(target: [reportName: 'OWASP ZAP - PWA', reportDir: 'reports/security/zap',
                                 reportFiles: 'zap-pwa.html', keepAll: true, alwaysLinkToLastBuild: true, allowMissing: true])
            publishHTML(target: [reportName: 'OWASP ZAP - API', reportDir: 'reports/security/zap',
                                 reportFiles: 'zap-api.html', keepAll: true, alwaysLinkToLastBuild: true, allowMissing: true])
        }
        success {
            echo "GO: la versión desplegada en ${env.TARGET_IP} cumple los criterios de salida."
        }
        failure {
            echo 'NO-GO o error: revise reports/go-no-go.md, reports/security/resumen.md y reports/cli/quality.json en los artefactos.'
        }
    }
}
