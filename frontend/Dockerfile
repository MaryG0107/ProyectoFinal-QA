# ---------- Etapa 1: compilación de la PWA ----------
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# ---------- Etapa 2: Nginx sin privilegios de root ----------
# Se usa el paquete nginx de Alpine en lugar de la imagen nginxinc: trivy evalúa ese paquete
# contra la base de seguridad de Alpine, que publica los parches (1.28.3-r6) antes que la
# imagen oficial (seguía en 1.28.2 con 10 CVE HIGH/CRITICAL). Ver DEF-03.
FROM alpine:3.23
RUN apk upgrade --no-cache \
 && apk add --no-cache nginx \
 && rm -f /etc/nginx/http.d/default.conf \
 && mkdir -p /etc/nginx/conf.d /etc/nginx/snippets /usr/share/nginx/html /run/nginx \
 && chown -R nginx:nginx /usr/share/nginx/html /var/lib/nginx /var/log/nginx /run/nginx
COPY nginx/nginx.conf /etc/nginx/nginx.conf
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build --chown=nginx:nginx /app/dist /usr/share/nginx/html
USER nginx
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
