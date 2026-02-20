#!/bin/bash

set -e

PROJECT_ROOT="/home/lcaraballo/Documentos/Sofka Projects/Projects/rlapp-backend"
SRCS="$PROJECT_ROOT/src/Services/WaitingRoom"

echo "🚀 Iniciando servicios RLAPP..."

# Iniciar API
echo "📡 Iniciando WaitingRoom.API..."
cd "$SRCS/WaitingRoom.API"
nohup dotnet run --configuration Release --urls "http://0.0.0.0:5000" > /tmp/api.nohup.log 2>&1 &
API_PID=$!
echo "   PID: $API_PID"

sleep 4

# Iniciar Worker (Outbox)
echo "⚙️  Iniciando WaitingRoom.Worker (Outbox)..."
cd "$SRCS/WaitingRoom.Worker"
nohup dotnet run --configuration Release > /tmp/worker.nohup.log 2>&1 &
WORKER_PID=$!
echo "   PID: $WORKER_PID"

sleep 3

# Iniciar Projections
echo "📊 Iniciando WaitingRoom.Projections..."
cd "$SRCS/WaitingRoom.Projections"
nohup dotnet run --configuration Release > /tmp/projections.nohup.log 2>&1 &
PROJ_PID=$!
echo "   PID: $PROJ_PID"

echo ""
echo "✅ Servicios en proceso de inicio..."
echo ""
echo "📝 Logs:"
echo "   API:         /tmp/api.nohup.log"
echo "   Worker:      /tmp/worker.nohup.log"
echo "   Projections: /tmp/projections.nohup.log"
echo ""

# Esperar a que API esté lista
echo "⏳ Esperando que API esté lista..."
for i in {1..30}; do
  if curl -s http://localhost:5000/health/live > /dev/null 2>&1; then
    echo "✅ API está lista en http://localhost:5000"
    break
  fi
  echo "   Intento $i/30..."
  sleep 2
done

echo ""
echo "🎯 Endpoints disponibles:"
echo "   POST /api/waiting-room/check-in"
echo "   GET  /health/live"
echo "   GET  /health/ready"
echo "   GET  /api/v1/waiting-room/{queueId}/queue-state"
echo "   GET  /api/v1/waiting-room/{queueId}/monitor"
echo ""
