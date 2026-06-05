import os
import logging
from datetime import date
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker
try:
    from models import Base, User, SubscriptionStatus
except ModuleNotFoundError:
    from backend.models import Base, User, SubscriptionStatus

logger = logging.getLogger(__name__)

DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://postgres:postgres@localhost:5432/lexwolf")


def _get_engine():
    return create_engine(DATABASE_URL)


def create_users_table():
    engine = _get_engine()
    Base.metadata.create_all(bind=engine, tables=[User.__table__])
    engine.dispose()


def check_subscription(email: str) -> str:
    """Return ACTIVE, INACTIVE, or UNKNOWN for the given email."""
    engine = _get_engine()
    Session = sessionmaker(bind=engine)
    session = Session()
    try:
        user = session.query(User).filter(User.email == email).first()
        if user is None:
            return "UNKNOWN"
        if user.subscription_status == SubscriptionStatus.active:
            if user.paid_until is None or user.paid_until >= date.today():
                return "ACTIVE"
            return "INACTIVE"
        if user.subscription_status == SubscriptionStatus.trial:
            if user.paid_until is None or user.paid_until >= date.today():
                return "ACTIVE"
            return "INACTIVE"
        return "INACTIVE"
    finally:
        session.close()
        engine.dispose()


def create_user(email: str, subscription_status: str = "inactive", paid_until=None) -> User:
    engine = _get_engine()
    Session = sessionmaker(bind=engine)
    session = Session()
    try:
        user = User(
            email=email,
            subscription_status=SubscriptionStatus(subscription_status),
            paid_until=paid_until,
        )
        session.add(user)
        session.commit()
        session.refresh(user)
        return user
    finally:
        session.close()
        engine.dispose()
