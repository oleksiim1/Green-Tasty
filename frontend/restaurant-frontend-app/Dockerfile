FROM node:22-bookworm-slim AS build

WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

ARG BACKEND_URL
ENV BACKEND_URL=$BACKEND_URL
ENV NODE_OPTIONS=--max-old-space-size=2048

RUN npm run build

FROM nginx:1.27-alpine

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/restaurant-frontend-app/browser /usr/share/nginx/html

EXPOSE 80
