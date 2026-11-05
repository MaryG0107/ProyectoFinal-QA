# ---------- Etapa 1: compilación de la PWA ----------
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# ---------- Etapa 2: Nginx sin privilegios de root ----------
# 1.28 es la rama estable vigente. "apk upgrade" instala los parches de Alpine publicados
# después de la imagen base (trivy reportaba libexpat, openssl, curl, libpng... sin parchear).
FROM nginxinc/nginx-unprivileged:1.28-alpine
USER root
RUN apk upgrade --no-cache
USER 101
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
