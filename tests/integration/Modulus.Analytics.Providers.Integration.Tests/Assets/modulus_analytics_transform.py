from datetime import datetime
from airflow import DAG
from airflow.operators.empty import EmptyOperator

with DAG(dag_id="modulus_analytics_transform", start_date=datetime(2026, 1, 1), schedule=None, catchup=False):
    EmptyOperator(task_id="transform")
