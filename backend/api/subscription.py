from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, EmailStr
from services.subscription_service import check_subscription, create_user

router = APIRouter(prefix="/api/subscription", tags=["subscription"])


class SubscriptionCheckResponse(BaseModel):
    email: str
    status: str  # ACTIVE | INACTIVE | UNKNOWN


class CreateUserRequest(BaseModel):
    email: str
    subscription_status: str = "inactive"
    paid_until: str | None = None


@router.get("/check/{email}", response_model=SubscriptionCheckResponse)
async def check_subscription_endpoint(email: str):
    status = check_subscription(email)
    return SubscriptionCheckResponse(email=email, status=status)


@router.post("/users", status_code=201)
async def create_user_endpoint(payload: CreateUserRequest):
    paid_until = None
    if payload.paid_until:
        from datetime import date
        try:
            paid_until = date.fromisoformat(payload.paid_until)
        except ValueError:
            raise HTTPException(status_code=400, detail="paid_until must be ISO date (YYYY-MM-DD)")
    try:
        user = create_user(payload.email, payload.subscription_status, paid_until)
        return {
            "id": user.id,
            "email": user.email,
            "subscription_status": user.subscription_status,
            "paid_until": str(user.paid_until) if user.paid_until else None,
            "created_at": str(user.created_at),
        }
    except Exception as e:
        raise HTTPException(status_code=400, detail=str(e))
