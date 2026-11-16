FROM python:3.13.18-slim
# zoneinfo needs the IANA database, which the slim image does not ship.
RUN pip install --no-cache-dir tzdata==2025.2
WORKDIR /app
COPY server.py .
USER nobody
EXPOSE 8090
CMD ["python", "-u", "server.py"]
