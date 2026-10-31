# syntax=docker/dockerfile:1
FROM node:24.22.2-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# Unprivileged nginx: runs as a non-root user and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.30.6-alpine AS runtime
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
