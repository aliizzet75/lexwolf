{
  "apps": [
    {
      "name": "lexwolf-frontend",
      "script": "npm",
      "args": "start",
      "cwd": "./frontend",
      "instances": 1,
      "exec_mode": "fork",
      "env": {
        "NODE_ENV": "production",
        "NEXT_PUBLIC_API_URL": "http://localhost:8000"
      }
    },
    {
      "name": "lexwolf-backend",
      "script": "uvicorn",
      "args": "main:app --host 0.0.0.0 --port 8000",
      "cwd": "./backend",
      "instances": 1,
      "exec_mode": "fork",
      "env": {
        "DATABASE_URL": "postgresql://lexwolf:lexwolf@localhost:5432/lexwolf"
      }
    }
  ]
}